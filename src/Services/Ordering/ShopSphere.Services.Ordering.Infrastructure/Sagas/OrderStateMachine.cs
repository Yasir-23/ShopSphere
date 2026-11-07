using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShopSphere.EventBus.Events;
using ShopSphere.Services.Ordering.Infrastructure.Persistence;

namespace ShopSphere.Services.Ordering.Infrastructure.Sagas;

public class OrderStateMachine : MassTransitStateMachine<OrderStateData>
{
    public State AwaitingInventory { get; private set; } = null!;
    public State AwaitingPayment { get; private set; } = null!;
    public State Completed { get; private set; } = null!;
    public State Cancelled { get; private set; } = null!;

    public Event<OrderCreatedIntegrationEvent> OrderCreated { get; private set; } = null!;
    public Event<ShopSphere.Services.Ordering.Application.Events.OrderCreatedIntegrationEvent> OrderCreatedApp { get; private set; } = null!;
    public Event<InventoryReservedIntegrationEvent> InventoryReserved { get; private set; } = null!;
    public Event<StockReservedEvent> StockReservedLegacy { get; private set; } = null!;
    public Event<InventoryReservationFailedIntegrationEvent> InventoryReservationFailed { get; private set; } = null!;
    public Event<StockReservationFailedEvent> StockReservationFailedLegacy { get; private set; } = null!;
    public Event<PaymentProcessedIntegrationEvent> PaymentProcessed { get; private set; } = null!;
    public Event<PaymentCompletedEvent> PaymentCompletedLegacy { get; private set; } = null!;
    public Event<PaymentFailedIntegrationEvent> PaymentFailed { get; private set; } = null!;
    public Event<PaymentFailedEvent> PaymentFailedLegacy { get; private set; } = null!;

    public OrderStateMachine(ILogger<OrderStateMachine> logger)
    {
        InstanceState(x => x.CurrentState);

        Event(() => OrderCreated, x => x
            .CorrelateById(m => m.Message.OrderId)
            .SelectId(m => m.Message.OrderId));

        Event(() => OrderCreatedApp, x => x
            .CorrelateById(m => m.Message.OrderId)
            .SelectId(m => m.Message.OrderId));

        Event(() => InventoryReserved, x => x.CorrelateById(m => m.Message.OrderId));
        Event(() => StockReservedLegacy, x => x.CorrelateById(m => m.Message.OrderId));

        Event(() => InventoryReservationFailed, x => x.CorrelateById(m => m.Message.OrderId));
        Event(() => StockReservationFailedLegacy, x => x.CorrelateById(m => m.Message.OrderId));

        Event(() => PaymentProcessed, x => x.CorrelateById(m => m.Message.OrderId));
        Event(() => PaymentCompletedLegacy, x => x.CorrelateById(m => m.Message.OrderId));

        Event(() => PaymentFailed, x => x.CorrelateById(m => m.Message.OrderId));
        Event(() => PaymentFailedLegacy, x => x.CorrelateById(m => m.Message.OrderId));

        Initially(
            When(OrderCreated)
                .Then(context =>
                {
                    logger.LogInformation("OrderCheckoutSaga [{OrderId}] - OrderCreatedIntegrationEvent received. Transitioning to AwaitingInventory.", context.Message.OrderId);
                    InitializeSaga(context.Saga, context.Message.OrderId, context.Message.CustomerId, context.Message.TotalAmount, context.Message.Items, context.Message.CreatedAtUtc);
                })
                .Publish(context => new ReserveInventoryCommand(
                    context.Saga.OrderId,
                    context.Saga.Items,
                    DateTimeOffset.UtcNow))
                .TransitionTo(AwaitingInventory),

            When(OrderCreatedApp)
                .Then(context =>
                {
                    logger.LogInformation("OrderCheckoutSaga [{OrderId}] - OrderCreatedIntegrationEvent (App) received. Transitioning to AwaitingInventory.", context.Message.OrderId);
                    var items = context.Message.Items?
                        .Select(i => new OrderItemDto(i.ProductId, i.ProductName, i.UnitPrice, i.Quantity, i.TotalPrice))
                        .ToList();
                    InitializeSaga(context.Saga, context.Message.OrderId, context.Message.CustomerId, context.Message.TotalAmount, items, context.Message.CreatedAtUtc);
                })
                .Publish(context => new ReserveInventoryCommand(
                    context.Saga.OrderId,
                    context.Saga.Items,
                    DateTimeOffset.UtcNow))
                .TransitionTo(AwaitingInventory)
        );

        During(AwaitingInventory,
            When(InventoryReserved)
                .Then(context =>
                {
                    logger.LogInformation("OrderCheckoutSaga [{OrderId}] - InventoryReserved received. Transitioning to AwaitingPayment.", context.Message.OrderId);
                    context.Saga.UpdatedAtUtc = DateTime.UtcNow;
                })
                .Publish(context => new ProcessPaymentCommand(
                    context.Saga.OrderId,
                    Guid.TryParse(context.Saga.CustomerId, out var cId) ? cId : Guid.NewGuid(),
                    context.Saga.TotalAmount,
                    DateTimeOffset.UtcNow))
                .TransitionTo(AwaitingPayment),

            When(StockReservedLegacy)
                .Then(context =>
                {
                    logger.LogInformation("OrderCheckoutSaga [{OrderId}] - StockReservedEvent (Legacy) received. Transitioning to AwaitingPayment.", context.Message.OrderId);
                    context.Saga.UpdatedAtUtc = DateTime.UtcNow;
                })
                .Publish(context => new ProcessPaymentCommand(
                    context.Saga.OrderId,
                    context.Message.CustomerId,
                    context.Saga.TotalAmount,
                    DateTimeOffset.UtcNow))
                .TransitionTo(AwaitingPayment),

            When(InventoryReservationFailed)
                .Then(context =>
                {
                    logger.LogWarning("OrderCheckoutSaga [{OrderId}] - InventoryReservationFailed: {Reason}. Cancelling order.", context.Message.OrderId, context.Message.Reason);
                    context.Saga.FailureReason = context.Message.Reason;
                    context.Saga.UpdatedAtUtc = DateTime.UtcNow;
                })
                .ThenAsync(async context =>
                {
                    await CancelOrderInDatabase(context);
                })
                .TransitionTo(Cancelled),

            When(StockReservationFailedLegacy)
                .Then(context =>
                {
                    logger.LogWarning("OrderCheckoutSaga [{OrderId}] - StockReservationFailedEvent (Legacy): {Reason}. Cancelling order.", context.Message.OrderId, context.Message.Reason);
                    context.Saga.FailureReason = context.Message.Reason;
                    context.Saga.UpdatedAtUtc = DateTime.UtcNow;
                })
                .ThenAsync(async context =>
                {
                    await CancelOrderInDatabase(context);
                })
                .TransitionTo(Cancelled)
        );

        During(AwaitingPayment,
            When(PaymentProcessed)
                .Then(context =>
                {
                    logger.LogInformation("OrderCheckoutSaga [{OrderId}] - PaymentProcessed received. Completing order.", context.Message.OrderId);
                    context.Saga.UpdatedAtUtc = DateTime.UtcNow;
                })
                .ThenAsync(async context =>
                {
                    await CompleteOrderInDatabase(context);
                })
                .TransitionTo(Completed),

            When(PaymentCompletedLegacy)
                .Then(context =>
                {
                    logger.LogInformation("OrderCheckoutSaga [{OrderId}] - PaymentCompleted (Legacy) received. Completing order.", context.Message.OrderId);
                    context.Saga.UpdatedAtUtc = DateTime.UtcNow;
                })
                .ThenAsync(async context =>
                {
                    await CompleteOrderInDatabase(context);
                })
                .TransitionTo(Completed),

            When(PaymentFailed)
                .Then(context =>
                {
                    logger.LogWarning("OrderCheckoutSaga [{OrderId}] - PaymentFailed: {Reason}. Compensating inventory reservation and cancelling order.", context.Message.OrderId, context.Message.Reason);
                    context.Saga.FailureReason = context.Message.Reason;
                    context.Saga.UpdatedAtUtc = DateTime.UtcNow;
                })
                .Publish(context => new ReleaseInventoryCommand(
                    context.Saga.OrderId,
                    context.Saga.Items,
                    context.Message.Reason,
                    DateTimeOffset.UtcNow))
                .ThenAsync(async context =>
                {
                    await CancelOrderInDatabase(context);
                })
                .TransitionTo(Cancelled),

            When(PaymentFailedLegacy)
                .Then(context =>
                {
                    logger.LogWarning("OrderCheckoutSaga [{OrderId}] - PaymentFailed (Legacy): {Reason}. Compensating inventory reservation and cancelling order.", context.Message.OrderId, context.Message.Reason);
                    context.Saga.FailureReason = context.Message.Reason;
                    context.Saga.UpdatedAtUtc = DateTime.UtcNow;
                })
                .Publish(context => new ReleaseInventoryCommand(
                    context.Saga.OrderId,
                    context.Saga.Items,
                    context.Message.Reason,
                    DateTimeOffset.UtcNow))
                .ThenAsync(async context =>
                {
                    await CancelOrderInDatabase(context);
                })
                .TransitionTo(Cancelled)
        );
    }

    private static void InitializeSaga(
        OrderStateData saga,
        Guid orderId,
        string customerId,
        decimal totalAmount,
        IEnumerable<OrderItemDto>? items,
        DateTime createdAtUtc)
    {
        saga.OrderId = orderId;
        saga.CustomerId = customerId ?? string.Empty;
        saga.TotalAmount = totalAmount;
        saga.Items = items?.ToList() ?? new List<OrderItemDto>();
        saga.CreatedAtUtc = createdAtUtc;
        saga.UpdatedAtUtc = DateTime.UtcNow;
    }

    private static OrderingDbContext? GetDbContext(BehaviorContext<OrderStateData> context)
    {
        if (context.TryGetPayload<OrderingDbContext>(out var ctx))
            return ctx;
        if (context.TryGetPayload<IServiceProvider>(out var sp))
            return sp.GetService<OrderingDbContext>();
        if (context.TryGetPayload<ConsumeContext>(out var cc))
        {
            if (cc.TryGetPayload<OrderingDbContext>(out var ccDb))
                return ccDb;
            if (cc.TryGetPayload<IServiceProvider>(out var ccSp))
                return ccSp.GetService<OrderingDbContext>();
        }
        return null;
    }

    private static async Task CompleteOrderInDatabase(BehaviorContext<OrderStateData> context)
    {
        var db = GetDbContext(context);
        if (db != null)
        {
            var order = await db.Orders.FindAsync(context.Saga.OrderId);
            if (order != null)
            {
                order.MarkCompleted();
                await db.SaveChangesAsync();
            }
        }
    }

    private static async Task CancelOrderInDatabase(BehaviorContext<OrderStateData> context)
    {
        var db = GetDbContext(context);
        if (db != null)
        {
            var order = await db.Orders.FindAsync(context.Saga.OrderId);
            if (order != null)
            {
                order.Cancel();
                await db.SaveChangesAsync();
            }
        }
    }
}
