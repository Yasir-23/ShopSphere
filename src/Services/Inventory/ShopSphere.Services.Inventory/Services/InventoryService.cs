using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShopSphere.Services.Inventory.Data;
using ShopSphere.Services.Inventory.Entities;
using ShopSphere.Services.Inventory.Models;

namespace ShopSphere.Services.Inventory.Services;

public class InventoryService : IInventoryService
{
    private readonly InventoryDbContext _context;
    private readonly ILogger<InventoryService> _logger;

    public InventoryService(InventoryDbContext context, ILogger<InventoryService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<InventoryStockResponse?> GetStockAsync(string productIdOrSku, CancellationToken cancellationToken = default)
    {
        var item = await _context.InventoryItems
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.ProductId == productIdOrSku || i.Sku == productIdOrSku, cancellationToken);

        return item is null ? null : MapToResponse(item);
    }

    public async Task<StockReservationResult> ReserveStockAsync(ReserveStockRequest request, CancellationToken cancellationToken = default)
    {
        // 1. Idempotency Check: if order already reserved, return success
        var existingReservations = await _context.InventoryReservations
            .Where(r => r.OrderId == request.OrderId && !r.IsReleased)
            .ToListAsync(cancellationToken);

        if (existingReservations.Count > 0)
        {
            _logger.LogInformation("Order {OrderId} is already reserved. Returning idempotent success.", request.OrderId);
            var alreadyReservedItems = existingReservations
                .Select(r => new StockReservationItem(r.ProductId, r.Quantity))
                .ToList();
            return StockReservationResult.Success(request.OrderId, alreadyReservedItems);
        }

        // 2. Fetch requested inventory items
        var requestedIdentifiers = request.Items.Select(i => i.ProductId).Distinct().ToList();
        var inventoryItems = await _context.InventoryItems
            .Where(i => requestedIdentifiers.Contains(i.ProductId) || requestedIdentifiers.Contains(i.Sku))
            .ToListAsync(cancellationToken);

        var itemsMap = new Dictionary<string, InventoryItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var invItem in inventoryItems)
        {
            itemsMap[invItem.ProductId] = invItem;
            itemsMap[invItem.Sku] = invItem;
        }

        // 3. Validate existence and stock availability across all requested items
        foreach (var reqItem in request.Items)
        {
            if (!itemsMap.TryGetValue(reqItem.ProductId, out var invItem))
            {
                _logger.LogWarning("Reservation failed: Product or SKU '{ProductId}' not found.", reqItem.ProductId);
                return StockReservationResult.NotFound($"Product or SKU '{reqItem.ProductId}' not found in inventory catalog.");
            }

            if (!invItem.HasSufficientStock(reqItem.Quantity))
            {
                _logger.LogWarning("Reservation failed: Insufficient stock for product '{ProductId}'. Available: {Available}, Requested: {Requested}",
                    invItem.ProductId, invItem.QuantityAvailable, reqItem.Quantity);
                return StockReservationResult.InsufficientStock(
                    $"Insufficient stock for product '{invItem.ProductId}' ({invItem.Sku}). Available: {invItem.QuantityAvailable}, Requested: {reqItem.Quantity}.");
            }
        }

        // 4. Atomically apply reservations
        try
        {
            var newReservations = new List<InventoryReservation>();
            foreach (var reqItem in request.Items)
            {
                var invItem = itemsMap[reqItem.ProductId];
                invItem.Reserve(reqItem.Quantity);

                newReservations.Add(new InventoryReservation
                {
                    OrderId = request.OrderId,
                    InventoryItemId = invItem.Id,
                    ProductId = invItem.ProductId,
                    Quantity = reqItem.Quantity,
                    ReservedAtUtc = DateTime.UtcNow
                });
            }

            await _context.InventoryReservations.AddRangeAsync(newReservations, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Successfully reserved stock for Order {OrderId} across {Count} items.",
                request.OrderId, request.Items.Count);

            return StockReservationResult.Success(request.OrderId, request.Items);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogError(ex, "Concurrency conflict encountered while reserving stock for Order {OrderId}.", request.OrderId);
            return StockReservationResult.ConcurrencyConflict("A concurrent stock update conflict occurred. Please retry your reservation.");
        }
    }

    public async Task<StockReleaseResult> ReleaseStockAsync(ReleaseStockRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var reservations = await _context.InventoryReservations
                .Include(r => r.InventoryItem)
                .Where(r => r.OrderId == request.OrderId && !r.IsReleased && !r.IsCommitted)
                .ToListAsync(cancellationToken);

            if (reservations.Count > 0)
            {
                foreach (var reservation in reservations)
                {
                    reservation.InventoryItem?.Release(reservation.Quantity);
                    reservation.IsReleased = true;
                }

                await _context.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Successfully released {Count} reservations for Order {OrderId}.", reservations.Count, request.OrderId);
                return StockReleaseResult.Success(request.OrderId);
            }

            // Fallback if reservations weren't explicitly recorded but explicit items are provided
            if (request.Items != null && request.Items.Count > 0)
            {
                var identifiers = request.Items.Select(i => i.ProductId).Distinct().ToList();
                var items = await _context.InventoryItems
                    .Where(i => identifiers.Contains(i.ProductId) || identifiers.Contains(i.Sku))
                    .ToListAsync(cancellationToken);

                var itemsMap = items.ToDictionary(i => i.ProductId, StringComparer.OrdinalIgnoreCase);

                foreach (var reqItem in request.Items)
                {
                    if (itemsMap.TryGetValue(reqItem.ProductId, out var invItem))
                    {
                        invItem.Release(reqItem.Quantity);
                    }
                }

                await _context.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Directly released specified stock items for Order {OrderId}.", request.OrderId);
            }

            return StockReleaseResult.Success(request.OrderId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to release stock for Order {OrderId}.", request.OrderId);
            return StockReleaseResult.Failed(request.OrderId, ex.Message);
        }
    }

    public async Task<StockCommitResult> CommitStockAsync(CommitStockRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var reservations = await _context.InventoryReservations
                .Include(r => r.InventoryItem)
                .Where(r => r.OrderId == request.OrderId && !r.IsReleased && !r.IsCommitted)
                .ToListAsync(cancellationToken);

            if (reservations.Count > 0)
            {
                foreach (var reservation in reservations)
                {
                    reservation.InventoryItem?.Commit(reservation.Quantity);
                    reservation.IsCommitted = true;
                }

                await _context.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Successfully committed stock deduction for Order {OrderId}.", request.OrderId);
                return StockCommitResult.Success(request.OrderId);
            }

            // Fallback if items were provided directly
            if (request.Items != null && request.Items.Count > 0)
            {
                var identifiers = request.Items.Select(i => i.ProductId).Distinct().ToList();
                var items = await _context.InventoryItems
                    .Where(i => identifiers.Contains(i.ProductId) || identifiers.Contains(i.Sku))
                    .ToListAsync(cancellationToken);

                var itemsMap = items.ToDictionary(i => i.ProductId, StringComparer.OrdinalIgnoreCase);

                foreach (var reqItem in request.Items)
                {
                    if (itemsMap.TryGetValue(reqItem.ProductId, out var invItem))
                    {
                        invItem.Commit(reqItem.Quantity);
                    }
                }

                await _context.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Directly committed specified stock items for Order {OrderId}.", request.OrderId);
            }

            return StockCommitResult.Success(request.OrderId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to commit stock deduction for Order {OrderId}.", request.OrderId);
            return StockCommitResult.Failed(request.OrderId, ex.Message);
        }
    }

    public async Task<InventoryStockResponse?> AdjustStockAsync(AdjustStockRequest request, CancellationToken cancellationToken = default)
    {
        var item = await _context.InventoryItems
            .FirstOrDefaultAsync(i => 
                (!string.IsNullOrEmpty(request.Sku) && i.Sku == request.Sku) ||
                (!string.IsNullOrEmpty(request.ProductId) && i.ProductId == request.ProductId), 
                cancellationToken);

        if (item is null)
        {
            _logger.LogWarning("Cannot adjust stock: Item with Sku '{Sku}' or ProductId '{ProductId}' not found.",
                request.Sku, request.ProductId);
            return null;
        }

        item.Restock(request.Quantity);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Adjusted/Restocked stock for SKU '{Sku}' (+{Quantity}). New QuantityOnHand: {OnHand}, New QuantityAvailable: {Available}.",
            item.Sku, request.Quantity, item.QuantityOnHand, item.QuantityAvailable);

        return MapToResponse(item);
    }

    private static InventoryStockResponse MapToResponse(InventoryItem item) => new()
    {
        Id = item.Id,
        ProductId = item.ProductId,
        Sku = item.Sku,
        QuantityOnHand = item.QuantityOnHand,
        QuantityReserved = item.QuantityReserved,
        QuantityAvailable = item.QuantityAvailable,
        RestockThreshold = item.RestockThreshold,
        UpdatedAtUtc = item.UpdatedAtUtc
    };
}

