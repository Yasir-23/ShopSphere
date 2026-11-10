using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShopSphere.Services.Inventory.Data;
using ShopSphere.Services.Inventory.Entities;
using ShopSphere.Services.Inventory.Models;

namespace ShopSphere.Services.Inventory.Services;

public class InventoryService : IInventoryService
{
    private const int MaxConcurrencyAttempts = 6;

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

    /// <summary>
    /// Runs a read-modify-write unit of work, retrying on optimistic concurrency conflicts
    /// (InventoryItem.RowVersion). The change tracker is cleared between attempts so every
    /// retry starts from freshly loaded rows.
    /// </summary>
    private async Task<T> WithConcurrencyRetryAsync<T>(string operation, Func<Task<T>> work, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await work();
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxConcurrencyAttempts)
            {
                _logger.LogInformation("Concurrency conflict during {Operation} (attempt {Attempt}); retrying.", operation, attempt);
                _context.ChangeTracker.Clear();
                await Task.Delay(Random.Shared.Next(10, 40) * attempt, cancellationToken);
            }
        }
    }

    public async Task<StockReservationResult> ReserveStockAsync(ReserveStockRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            return await WithConcurrencyRetryAsync("reserve", () => ReserveOnceAsync(request, cancellationToken), cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogError(ex, "Concurrency conflict persisted while reserving stock for Order {OrderId}.", request.OrderId);
            _context.ChangeTracker.Clear();
            return StockReservationResult.ConcurrencyConflict("A concurrent stock update conflict occurred. Please retry your reservation.");
        }
    }

    private async Task<StockReservationResult> ReserveOnceAsync(ReserveStockRequest request, CancellationToken cancellationToken)
    {
        // 1. Idempotency: an order is reserved at most once.
        var existingReservations = await _context.InventoryReservations
            .Where(r => r.OrderId == request.OrderId)
            .ToListAsync(cancellationToken);

        if (existingReservations.Count > 0)
        {
            _logger.LogInformation("Order {OrderId} already has reservations. Returning idempotent success.", request.OrderId);
            return StockReservationResult.Success(request.OrderId,
                existingReservations.Select(r => new StockReservationItem(r.ProductId, r.Quantity)).ToList());
        }

        // 2. Fetch requested inventory items (aggregate duplicate lines for the same product).
        var requested = request.Items
            .GroupBy(i => i.ProductId, StringComparer.OrdinalIgnoreCase)
            .Select(g => new StockReservationItem(g.Key, g.Sum(i => i.Quantity)))
            .ToList();
        var identifiers = requested.Select(i => i.ProductId).ToList();

        var inventoryItems = await _context.InventoryItems
            .Where(i => identifiers.Contains(i.ProductId) || identifiers.Contains(i.Sku))
            .ToListAsync(cancellationToken);

        var itemsMap = new Dictionary<string, InventoryItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var invItem in inventoryItems)
        {
            itemsMap[invItem.ProductId] = invItem;
            itemsMap[invItem.Sku] = invItem;
        }

        // 3. Validate all lines before touching anything (all-or-nothing).
        foreach (var reqItem in requested)
        {
            if (!itemsMap.TryGetValue(reqItem.ProductId, out var invItem))
            {
                _logger.LogWarning("Reservation failed: Product or SKU '{ProductId}' not found.", reqItem.ProductId);
                return StockReservationResult.NotFound($"Product or SKU '{reqItem.ProductId}' not found in inventory catalog.");
            }

            if (!invItem.HasSufficientStock(reqItem.Quantity))
            {
                _logger.LogWarning("Reservation failed: Insufficient stock for '{ProductId}'. Available: {Available}, Requested: {Requested}",
                    invItem.ProductId, invItem.QuantityAvailable, reqItem.Quantity);
                return StockReservationResult.InsufficientStock(
                    $"Insufficient stock for product '{invItem.ProductId}' ({invItem.Sku}). Available: {invItem.QuantityAvailable}, Requested: {reqItem.Quantity}.");
            }
        }

        // 4. Apply; RowVersion guards against concurrent writers.
        foreach (var reqItem in requested)
        {
            var invItem = itemsMap[reqItem.ProductId];
            invItem.Reserve(reqItem.Quantity);

            _context.InventoryReservations.Add(new InventoryReservation
            {
                OrderId = request.OrderId,
                InventoryItemId = invItem.Id,
                ProductId = invItem.ProductId,
                Quantity = reqItem.Quantity,
                ReservedAtUtc = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Reserved stock for Order {OrderId} across {Count} items.", request.OrderId, requested.Count);
        return StockReservationResult.Success(request.OrderId, requested);
    }

    public async Task<StockReleaseResult> ReleaseStockAsync(ReleaseStockRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            return await WithConcurrencyRetryAsync("release", async () =>
            {
                var reservations = await _context.InventoryReservations
                    .Include(r => r.InventoryItem)
                    .Where(r => r.OrderId == request.OrderId)
                    .ToListAsync(cancellationToken);

                if (reservations.Count > 0)
                {
                    var open = reservations.Where(r => !r.IsReleased && !r.IsCommitted).ToList();
                    if (open.Count == 0)
                    {
                        // Already released or committed: idempotent no-op (never double-release).
                        _logger.LogInformation("Release for Order {OrderId} already processed; nothing to do.", request.OrderId);
                        return StockReleaseResult.Success(request.OrderId);
                    }

                    foreach (var reservation in open)
                    {
                        reservation.InventoryItem?.Release(reservation.Quantity);
                        reservation.IsReleased = true;
                    }

                    await _context.SaveChangesAsync(cancellationToken);
                    _logger.LogInformation("Released {Count} reservations for Order {OrderId}.", open.Count, request.OrderId);
                    return StockReleaseResult.Success(request.OrderId);
                }

                // No reservation was ever recorded for this order. Only an explicit item list
                // (manual API use) releases stock directly; saga compensation sends none.
                if (request.Items is { Count: > 0 })
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
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to release stock for Order {OrderId}.", request.OrderId);
            _context.ChangeTracker.Clear();
            return StockReleaseResult.Failed(request.OrderId, ex.Message);
        }
    }

    public async Task<StockCommitResult> CommitStockAsync(CommitStockRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            return await WithConcurrencyRetryAsync("commit", async () =>
            {
                var reservations = await _context.InventoryReservations
                    .Include(r => r.InventoryItem)
                    .Where(r => r.OrderId == request.OrderId)
                    .ToListAsync(cancellationToken);

                if (reservations.Count > 0)
                {
                    var open = reservations.Where(r => !r.IsReleased && !r.IsCommitted).ToList();
                    if (open.Count == 0)
                    {
                        _logger.LogInformation("Commit for Order {OrderId} already processed; nothing to do.", request.OrderId);
                        return StockCommitResult.Success(request.OrderId);
                    }

                    foreach (var reservation in open)
                    {
                        reservation.InventoryItem?.Commit(reservation.Quantity);
                        reservation.IsCommitted = true;
                    }

                    await _context.SaveChangesAsync(cancellationToken);
                    _logger.LogInformation("Committed stock deduction for Order {OrderId}.", request.OrderId);
                    return StockCommitResult.Success(request.OrderId);
                }

                if (request.Items is { Count: > 0 })
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
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to commit stock deduction for Order {OrderId}.", request.OrderId);
            _context.ChangeTracker.Clear();
            return StockCommitResult.Failed(request.OrderId, ex.Message);
        }
    }

    public Task<InventoryStockResponse?> AdjustStockAsync(AdjustStockRequest request, CancellationToken cancellationToken = default) =>
        WithConcurrencyRetryAsync<InventoryStockResponse?>("adjust", async () =>
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

            var newOnHand = item.QuantityOnHand + request.Quantity;
            if (newOnHand < item.QuantityReserved)
            {
                throw new InsufficientStockForAdjustmentException(
                    $"Cannot remove {-request.Quantity} units from '{item.Sku}': {item.QuantityOnHand} on hand, {item.QuantityReserved} reserved for open orders.");
            }

            item.Adjust(newOnHand);
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Adjusted stock for SKU '{Sku}' by {Delta}. On hand: {OnHand}, available: {Available}.",
                item.Sku, request.Quantity, item.QuantityOnHand, item.QuantityAvailable);

            return MapToResponse(item);
        }, cancellationToken);

    public async Task<StockItemCreationResult> CreateStockItemAsync(CreateStockItemRequest request, CancellationToken cancellationToken = default)
    {
        var productId = request.ProductId.Trim();
        var sku = request.Sku.Trim().ToUpperInvariant();

        var existing = await _context.InventoryItems
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.ProductId == productId || i.Sku == sku, cancellationToken);
        if (existing is not null)
        {
            return StockItemCreationResult.Conflict(existing.ProductId == productId
                ? $"A stock record already exists for product '{productId}'."
                : $"A stock record already exists for SKU '{sku}'.");
        }

        var item = new InventoryItem
        {
            ProductId = productId,
            Sku = sku,
            QuantityOnHand = request.Quantity,
            QuantityReserved = 0,
            RestockThreshold = request.RestockThreshold ?? 5,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _context.InventoryItems.Add(item);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            // Unique index race with a concurrent creator.
            _logger.LogWarning(ex, "Stock record creation for {ProductId}/{Sku} raced with another request.", productId, sku);
            _context.ChangeTracker.Clear();
            return StockItemCreationResult.Conflict($"A stock record already exists for product '{productId}' or SKU '{sku}'.");
        }

        _logger.LogInformation("Provisioned stock record for {ProductId} ({Sku}) with {Quantity} units.", productId, sku, request.Quantity);
        return StockItemCreationResult.Created(MapToResponse(item));
    }

    public async Task<StockItemDeletionResult> DeleteStockItemAsync(string productIdOrSku, CancellationToken cancellationToken = default)
    {
        var item = await _context.InventoryItems
            .FirstOrDefaultAsync(i => i.ProductId == productIdOrSku || i.Sku == productIdOrSku, cancellationToken);
        if (item is null)
        {
            return StockItemDeletionResult.NotFound;
        }

        var hasOpenReservations = await _context.InventoryReservations
            .AnyAsync(r => r.InventoryItemId == item.Id && !r.IsReleased && !r.IsCommitted, cancellationToken);
        if (hasOpenReservations)
        {
            return StockItemDeletionResult.HasOpenReservations;
        }

        _context.InventoryItems.Remove(item);
        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Deleted stock record for {ProductId} ({Sku}).", item.ProductId, item.Sku);
        return StockItemDeletionResult.Deleted;
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
