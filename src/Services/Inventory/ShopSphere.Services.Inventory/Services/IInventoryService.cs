using ShopSphere.Services.Inventory.Models;

namespace ShopSphere.Services.Inventory.Services;

public interface IInventoryService
{
    Task<InventoryStockResponse?> GetStockAsync(string productIdOrSku, CancellationToken cancellationToken = default);
    Task<StockReservationResult> ReserveStockAsync(ReserveStockRequest request, CancellationToken cancellationToken = default);
    Task<StockReleaseResult> ReleaseStockAsync(ReleaseStockRequest request, CancellationToken cancellationToken = default);
    Task<StockCommitResult> CommitStockAsync(CommitStockRequest request, CancellationToken cancellationToken = default);

    /// <summary>Adds (positive) or removes (negative) units. Returns null when the item does not exist.</summary>
    Task<InventoryStockResponse?> AdjustStockAsync(AdjustStockRequest request, CancellationToken cancellationToken = default);

    /// <summary>Creates the stock record for a newly catalogued product.</summary>
    Task<StockItemCreationResult> CreateStockItemAsync(CreateStockItemRequest request, CancellationToken cancellationToken = default);

    /// <summary>Removes a stock record (e.g. when a product is deleted). Refused while reservations are open.</summary>
    Task<StockItemDeletionResult> DeleteStockItemAsync(string productIdOrSku, CancellationToken cancellationToken = default);
}

/// <summary>Raised when a negative adjustment would leave fewer units on hand than are reserved.</summary>
public class InsufficientStockForAdjustmentException(string message) : InvalidOperationException(message);
