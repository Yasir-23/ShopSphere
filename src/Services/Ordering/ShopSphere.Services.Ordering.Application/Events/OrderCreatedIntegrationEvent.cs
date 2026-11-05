namespace ShopSphere.Services.Ordering.Application.Events;

public record OrderItemDto(
    string ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    decimal TotalPrice
);

public record OrderCreatedIntegrationEvent(
    Guid OrderId,
    string CustomerId,
    decimal TotalAmount,
    IReadOnlyList<OrderItemDto> Items,
    DateTime CreatedAtUtc
);

