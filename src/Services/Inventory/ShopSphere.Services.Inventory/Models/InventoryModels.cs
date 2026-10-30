namespace ShopSphere.Services.Inventory.Models;

public record InventoryStockResponse
{
    public Guid Id { get; init; }
    public string ProductId { get; init; } = string.Empty;
    public string Sku { get; init; } = string.Empty;
    public int QuantityOnHand { get; init; }
    public int QuantityReserved { get; init; }
    public int QuantityAvailable { get; init; }
    public int AvailableQuantity => QuantityAvailable;
    public int ReservedQuantity => QuantityReserved;
    public int RestockThreshold { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
}

public record StockReservationItem(
    string ProductId,
    int Quantity);

public record ReserveStockRequest(
    Guid OrderId,
    List<StockReservationItem> Items);

public record StockReservationResult
{
    public Guid OrderId { get; init; }
    public bool IsSuccess { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? ErrorMessage { get; init; }
    public int StatusCode { get; init; }
    public List<StockReservationItem>? ReservedItems { get; init; }

    public static StockReservationResult Success(Guid orderId, List<StockReservationItem> items) => new()
    {
        OrderId = orderId,
        IsSuccess = true,
        Status = "Reserved",
        StatusCode = 200,
        ReservedItems = items
    };

    public static StockReservationResult InsufficientStock(string message) => new()
    {
        IsSuccess = false,
        Status = "InsufficientStock",
        StatusCode = 409,
        ErrorMessage = message
    };

    public static StockReservationResult NotFound(string message) => new()
    {
        IsSuccess = false,
        Status = "NotFound",
        StatusCode = 404,
        ErrorMessage = message
    };

    public static StockReservationResult ConcurrencyConflict(string message) => new()
    {
        IsSuccess = false,
        Status = "ConcurrencyConflict",
        StatusCode = 409,
        ErrorMessage = message
    };
}

public record ReleaseStockRequest(
    Guid OrderId,
    List<StockReservationItem>? Items = null);

public record StockReleaseResult
{
    public Guid OrderId { get; init; }
    public bool IsSuccess { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? ErrorMessage { get; init; }

    public static StockReleaseResult Success(Guid orderId) => new()
    {
        OrderId = orderId,
        IsSuccess = true,
        Status = "Released"
    };

    public static StockReleaseResult Failed(Guid orderId, string message) => new()
    {
        OrderId = orderId,
        IsSuccess = false,
        Status = "Failed",
        ErrorMessage = message
    };
}

public record CommitStockRequest(
    Guid OrderId,
    List<StockReservationItem>? Items = null);

public record StockCommitResult
{
    public Guid OrderId { get; init; }
    public bool IsSuccess { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? ErrorMessage { get; init; }

    public static StockCommitResult Success(Guid orderId) => new()
    {
        OrderId = orderId,
        IsSuccess = true,
        Status = "Committed"
    };

    public static StockCommitResult Failed(Guid orderId, string message) => new()
    {
        OrderId = orderId,
        IsSuccess = false,
        Status = "Failed",
        ErrorMessage = message
    };
}

public record AdjustStockRequest(
    string? Sku,
    string? ProductId,
    int Quantity,
    string? Reason = null);

