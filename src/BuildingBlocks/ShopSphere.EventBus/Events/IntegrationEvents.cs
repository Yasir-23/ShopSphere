namespace ShopSphere.EventBus.Events;

public abstract record IntegrationEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTimeOffset CreationDate { get; init; } = DateTimeOffset.UtcNow;
}

public record OrderItemDto(string Sku, int Quantity);

// OrderStartedEvent
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

// StockReservedEvent
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

// StockReservationFailedEvent
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

// ReleaseStockCommand
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
