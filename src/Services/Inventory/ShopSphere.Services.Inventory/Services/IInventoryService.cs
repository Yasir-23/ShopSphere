using ShopSphere.Services.Inventory.Models;

namespace ShopSphere.Services.Inventory.Services;

public interface IInventoryService
{
    Task<InventoryStockResponse?> GetStockAsync(string productIdOrSku, CancellationToken cancellationToken = default);
    Task<StockReservationResult> ReserveStockAsync(ReserveStockRequest request, CancellationToken cancellationToken = default);
    Task<StockReleaseResult> ReleaseStockAsync(ReleaseStockRequest request, CancellationToken cancellationToken = default);
    Task<StockCommitResult> CommitStockAsync(CommitStockRequest request, CancellationToken cancellationToken = default);
    Task<InventoryStockResponse?> AdjustStockAsync(AdjustStockRequest request, CancellationToken cancellationToken = default);
}

