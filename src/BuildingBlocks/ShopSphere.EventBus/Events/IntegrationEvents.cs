namespace ShopSphere.EventBus.Events;

public abstract record IntegrationEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTimeOffset CreationDate { get; init; } = DateTimeOffset.UtcNow;
}

public record OrderItemDto(
    string ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    decimal TotalPrice)
{
    public string Sku => ProductId;

    public OrderItemDto() : this(string.Empty, string.Empty, 0m, 0, 0m) { }

    public OrderItemDto(string sku, int quantity) : this(sku, sku, 0m, quantity, 0m) { }
}

// OrderCreatedIntegrationEvent
public interface IOrderCreatedIntegrationEvent
{
    Guid OrderId { get; }
    string CustomerId { get; }
    decimal TotalAmount { get; }
    IReadOnlyList<OrderItemDto> Items { get; }
    DateTime CreatedAtUtc { get; }
}

public record OrderCreatedIntegrationEvent(
    Guid OrderId,
    string CustomerId,
    decimal TotalAmount,
    IReadOnlyList<OrderItemDto> Items,
    DateTime CreatedAtUtc
) : IntegrationEvent, IOrderCreatedIntegrationEvent;

// OrderStartedEvent (Legacy / Alt naming compatibility)
public interface IOrderStartedEvent
{
    Guid OrderId { get; }
    Guid CustomerId { get; }
    IReadOnlyList<OrderItemDto> Items { get; }
    decimal TotalAmount { get; }
    DateTimeOffset Timestamp { get; }
}

public record OrderStartedEvent(
    Guid OrderId,
    Guid CustomerId,
    IReadOnlyList<OrderItemDto> Items,
    decimal TotalAmount,
    DateTimeOffset Timestamp
) : IntegrationEvent, IOrderStartedEvent;

// ReserveInventoryCommand
public interface IReserveInventoryCommand
{
    Guid OrderId { get; }
    IReadOnlyList<OrderItemDto> Items { get; }
    DateTimeOffset Timestamp { get; }
}

public record ReserveInventoryCommand(
    Guid OrderId,
    IReadOnlyList<OrderItemDto> Items,
    DateTimeOffset Timestamp
) : IntegrationEvent, IReserveInventoryCommand
{
    public ReserveInventoryCommand(Guid orderId, IReadOnlyList<OrderItemDto> items)
        : this(orderId, items, DateTimeOffset.UtcNow) { }
}

// InventoryReservedIntegrationEvent
public interface IInventoryReservedIntegrationEvent
{
    Guid OrderId { get; }
    DateTime ReservedAtUtc { get; }
}

public record InventoryReservedIntegrationEvent(
    Guid OrderId,
    DateTime ReservedAtUtc
) : IntegrationEvent, IInventoryReservedIntegrationEvent;

// InventoryReservationFailedIntegrationEvent
public interface IInventoryReservationFailedIntegrationEvent
{
    Guid OrderId { get; }
    string Reason { get; }
    DateTime FailedAtUtc { get; }
}

public record InventoryReservationFailedIntegrationEvent(
    Guid OrderId,
    string Reason,
    DateTime FailedAtUtc
) : IntegrationEvent, IInventoryReservationFailedIntegrationEvent;

// StockReservedEvent (Legacy / Alt naming compatibility)
public interface IStockReservedEvent
{
    Guid OrderId { get; }
    Guid CustomerId { get; }
    DateTimeOffset Timestamp { get; }
}

public record StockReservedEvent(
    Guid OrderId,
    Guid CustomerId,
    DateTimeOffset Timestamp
) : IntegrationEvent, IStockReservedEvent;

// StockReservationFailedEvent (Legacy / Alt naming compatibility)
public interface IStockReservationFailedEvent
{
    Guid OrderId { get; }
    Guid CustomerId { get; }
    string Reason { get; }
    DateTimeOffset Timestamp { get; }
}

public record StockReservationFailedEvent(
    Guid OrderId,
    Guid CustomerId,
    string Reason,
    DateTimeOffset Timestamp
) : IntegrationEvent, IStockReservationFailedEvent;

// ProcessPaymentCommand
public interface IProcessPaymentCommand
{
    Guid OrderId { get; }
    Guid CustomerId { get; }
    decimal Amount { get; }
    DateTimeOffset Timestamp { get; }
}

public record ProcessPaymentCommand(
    Guid OrderId,
    Guid CustomerId,
    decimal Amount,
    DateTimeOffset Timestamp
) : IntegrationEvent, IProcessPaymentCommand;

// PaymentCompletedEvent
public interface IPaymentCompletedEvent
{
    Guid PaymentId { get; }
    Guid OrderId { get; }
    decimal Amount { get; }
    string TransactionId { get; }
    DateTimeOffset Timestamp { get; }
}

public record PaymentCompletedEvent(
    Guid PaymentId,
    Guid OrderId,
    decimal Amount,
    string TransactionId,
    DateTimeOffset Timestamp
) : IntegrationEvent, IPaymentCompletedEvent;

// PaymentProcessedIntegrationEvent
public interface IPaymentProcessedIntegrationEvent
{
    Guid PaymentId { get; }
    Guid OrderId { get; }
    decimal Amount { get; }
    string TransactionId { get; }
    DateTime ProcessedAtUtc { get; }
}

public record PaymentProcessedIntegrationEvent(
    Guid PaymentId,
    Guid OrderId,
    decimal Amount,
    string TransactionId,
    DateTime ProcessedAtUtc
) : IntegrationEvent, IPaymentProcessedIntegrationEvent;

// PaymentFailedEvent
public interface IPaymentFailedEvent
{
    Guid OrderId { get; }
    string Reason { get; }
    DateTimeOffset Timestamp { get; }
}

public record PaymentFailedEvent(
    Guid OrderId,
    string Reason,
    DateTimeOffset Timestamp
) : IntegrationEvent, IPaymentFailedEvent;

// PaymentFailedIntegrationEvent
public interface IPaymentFailedIntegrationEvent
{
    Guid OrderId { get; }
    string Reason { get; }
    DateTime FailedAtUtc { get; }
}

public record PaymentFailedIntegrationEvent(
    Guid OrderId,
    string Reason,
    DateTime FailedAtUtc
) : IntegrationEvent, IPaymentFailedIntegrationEvent;

// ReleaseInventoryCommand
public interface IReleaseInventoryCommand
{
    Guid OrderId { get; }
    IReadOnlyList<OrderItemDto> Items { get; }
    string Reason { get; }
    DateTimeOffset Timestamp { get; }
}

public record ReleaseInventoryCommand(
    Guid OrderId,
    IReadOnlyList<OrderItemDto> Items,
    string Reason,
    DateTimeOffset Timestamp
) : IntegrationEvent, IReleaseInventoryCommand;

// ReleaseStockCommand (Legacy / Alt naming compatibility)
public interface IReleaseStockCommand
{
    Guid OrderId { get; }
    IReadOnlyList<OrderItemDto> Items { get; }
    string Reason { get; }
    DateTimeOffset Timestamp { get; }
}

public record ReleaseStockCommand(
    Guid OrderId,
    IReadOnlyList<OrderItemDto> Items,
    string Reason,
    DateTimeOffset Timestamp
) : IntegrationEvent, IReleaseStockCommand;
