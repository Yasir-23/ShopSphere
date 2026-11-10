namespace ShopSphere.Services.Ordering.Application.DTOs;

public record OrderItemResponse(
    Guid Id,
    string ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    decimal TotalPrice
);

public record OrderResponse(
    Guid Id,
    string CustomerId,
    string CustomerEmail,
    AddressDto ShippingAddress,
    IReadOnlyList<OrderItemResponse> Items,
    decimal TotalAmount,
    string Status,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    string? CancellationReason = null
);

public record OrderSummaryResponse(
    Guid Id,
    string CustomerId,
    decimal TotalAmount,
    string Status,
    int TotalItems,
    DateTime CreatedAtUtc
);

public record PagedResult<T>(
    IReadOnlyList<T> Items,
    int PageNumber,
    int PageSize,
    int TotalCount,
    int TotalPages
)
{
    public static PagedResult<T> Create(IReadOnlyList<T> items, int totalCount, int pageNumber, int pageSize)
    {
        var totalPages = pageSize > 0 ? (int)Math.Ceiling(totalCount / (double)pageSize) : 0;
        return new PagedResult<T>(items, pageNumber, pageSize, totalCount, totalPages);
    }
}

