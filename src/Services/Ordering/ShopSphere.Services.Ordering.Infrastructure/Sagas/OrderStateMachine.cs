using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShopSphere.EventBus.Events;
using ShopSphere.Services.Ordering.Domain.Entities;
using ShopSphere.Services.Ordering.Domain.Enums;
using ShopSphere.Services.Ordering.Infrastructure.Persistence;

namespace ShopSphere.Services.Ordering.Infrastructure.Sagas;

/// <summary>
/// Checkout saga (orchestrator).
///
///   Initial ──OrderCreated──▶ AwaitingInventory ──InventoryReserved──▶ AwaitingPayment ──PaymentProcessed──▶ Completed
///                                   │                                        │
///                                   │ InventoryReservationFailed             │ PaymentFailed / OrderCancelled
///                                   ▼                                        ▼
///                               Cancelled ◀──────InventoryReleased────── Compensating
///
/// The Orders aggregate is updated at every step: Pending → StockReserved → Completed, or → Cancelled
/// (with a customer-facing CancellationReason). After payment, a CommitInventoryCommand turns the
/// reservation into a permanent stock deduction.
/// </summary>
public class OrderStateMachine : MassTransitStateMachine<OrderStateData>
{
    public const string DefaultPaymentMethod = "card";

    public State AwaitingInventory { get; private set; } = null!;
    public State AwaitingPayment { get; private set; } = null!;
    public State Compensating { get; private set; } = null!;
    public State Completed { get; private set; } = null!;
    public State Cancelled { get; private set; } = null!;

    public Event<OrderCreatedIntegrationEvent> OrderCreated { get; private set; } = null!;
    public Event<ShopSphere.Services.Ordering.Application.Events.OrderCreatedIntegrationEvent> OrderCreatedApp { get; private set; } = null!;
    public Event<InventoryReservedIntegrationEvent> InventoryReserved { get; private set; } = null!;
    public Event<StockReservedEvent> StockReservedLegacy { get; private set; } = null!;
    public Event<InventoryReservationFailedIntegrationEvent> InventoryReservationFailed { get; private set; } = null!;
    public Event<StockReservationFailedEvent> StockReservationFailedLegacy { get; private set; } = null!;
    public Event<InventoryReleasedIntegrationEvent> InventoryReleased { get; private set; } = null!;
    public Event<PaymentProcessedIntegrationEvent> PaymentProcessed { get; private set; } = null!;
    public Event<PaymentCompletedEvent> PaymentCompletedLegacy { get; private set; } = null!;
    public Event<PaymentFailedIntegrationEvent> PaymentFailed { get; private set; } = null!;
    public Event<PaymentFailedEvent> PaymentFailedLegacy { get; private set; } = null!;
    public Event<OrderCancelledIntegrationEvent> OrderCancelled { get; private set; } = null!;

    public OrderStateMachine(ILogger<OrderStateMachine> logger)
    {
        InstanceState(x => x.CurrentState);

        Event(() => OrderCreated, x => x.CorrelateById(m => m.Message.OrderId).SelectId(m => m.Message.OrderId));
        Event(() => OrderCreatedApp, x => x.CorrelateById(m => m.Message.OrderId).SelectId(m => m.Message.OrderId));

        // Events for unknown orders (e.g. legacy/foreign messages) are dropped instead of faulting.
        Event(() => InventoryReserved, x => { x.CorrelateById(m => m.Message.OrderId); x.OnMissingInstance(m => m.Discard()); });
        Event(() => StockReservedLegacy, x => { x.CorrelateById(m => m.Message.OrderId); x.OnMissingInstance(m => m.Discard()); });
        Event(() => InventoryReservationFailed, x => { x.CorrelateById(m => m.Message.OrderId); x.OnMissingInstance(m => m.Discard()); });
        Event(() => StockReservationFailedLegacy, x => { x.CorrelateById(m => m.Message.OrderId); x.OnMissingInstance(m => m.Discard()); });
        Event(() => InventoryReleased, x => { x.CorrelateById(m => m.Message.OrderId); x.OnMissingInstance(m => m.Discard()); });
        Event(() => PaymentProcessed, x => { x.CorrelateById(m => m.Message.OrderId); x.OnMissingInstance(m => m.Discard()); });
        Event(() => PaymentCompletedLegacy, x => { x.CorrelateById(m => m.Message.OrderId); x.OnMissingInstance(m => m.Discard()); });
        Event(() => PaymentFailed, x => { x.CorrelateById(m => m.Message.OrderId); x.OnMissingInstance(m => m.Discard()); });
        Event(() => PaymentFailedLegacy, x => { x.CorrelateById(m => m.Message.OrderId); x.OnMissingInstance(m => m.Discard()); });
        Event(() => OrderCancelled, x => { x.CorrelateById(m => m.Message.OrderId); x.OnMissingInstance(m => m.Discard()); });

        // ── Submitted ──────────────────────────────────────────────────────────────
        Initially(
            When(OrderCreated)
                .Then(ctx =>
                {
                    logger.LogInformation("Saga[{OrderId}] OrderCreated → AwaitingInventory (reserve stock).", ctx.Message.OrderId);
                    InitializeSaga(ctx.Saga, ctx.Message.OrderId, ctx.Message.CustomerId, ctx.Message.TotalAmount,
                        ctx.Message.Items, ctx.Message.CreatedAtUtc, ctx.Message.PaymentMethod);
                })
                .Publish(ctx => new ReserveInventoryCommand(ctx.Saga.OrderId, ctx.Saga.Items, DateTimeOffset.UtcNow))
                .TransitionTo(AwaitingInventory),

            When(OrderCreatedApp)
                .Then(ctx =>
                {
                    logger.LogInformation("Saga[{OrderId}] OrderCreated → AwaitingInventory (reserve stock).", ctx.Message.OrderId);
                    var items = ctx.Message.Items?
                        .Select(i => new OrderItemDto(i.ProductId, i.ProductName, i.UnitPrice, i.Quantity, i.TotalPrice))
                        .ToList();
                    InitializeSaga(ctx.Saga, ctx.Message.OrderId, ctx.Message.CustomerId, ctx.Message.TotalAmount,
                        items, ctx.Message.CreatedAtUtc, ctx.Message.PaymentMethod);
                })
                .Publish(ctx => new ReserveInventoryCommand(ctx.Saga.OrderId, ctx.Saga.Items, DateTimeOffset.UtcNow))
                .TransitionTo(AwaitingInventory)
        );

        // ── AwaitingInventory ─────────────────────────────────────────────────────
        During(AwaitingInventory,
            When(InventoryReserved)
                .ThenAsync(ctx => OnStockReserved(ctx, logger))
                .Publish(ctx => BuildPaymentCommand(ctx.Saga))
                .TransitionTo(AwaitingPayment),

            When(StockReservedLegacy)
                .ThenAsync(ctx => OnStockReserved(ctx, logger))
                .Publish(ctx => BuildPaymentCommand(ctx.Saga))
                .TransitionTo(AwaitingPayment),

            When(InventoryReservationFailed)
                .ThenAsync(ctx => FailWithoutReservation(ctx, logger, ctx.Message.Reason))
                .TransitionTo(Cancelled),

            When(StockReservationFailedLegacy)
                .ThenAsync(ctx => FailWithoutReservation(ctx, logger, ctx.Message.Reason))
                .TransitionTo(Cancelled),

            // Cancelled before stock was confirmed: release defensively (idempotent) in case a
            // reservation is in flight, then finish once Inventory acknowledges.
            When(OrderCancelled)
                .Then(ctx => Fail(ctx.Saga, ctx.Message.Reason))
                .Publish(ctx => new ReleaseInventoryCommand(ctx.Saga.OrderId, ctx.Saga.Items, ctx.Saga.FailureReason!, DateTimeOffset.UtcNow))
                .TransitionTo(Compensating),

            Ignore(PaymentProcessed), Ignore(PaymentCompletedLegacy), Ignore(PaymentFailed), Ignore(PaymentFailedLegacy),
            Ignore(InventoryReleased)
        );

        // ── AwaitingPayment ───────────────────────────────────────────────────────
        During(AwaitingPayment,
            When(PaymentProcessed)
                .ThenAsync(ctx => OnPaid(ctx, logger))
                .Publish(ctx => new CommitInventoryCommand(ctx.Saga.OrderId, DateTimeOffset.UtcNow))
                .TransitionTo(Completed),

            When(PaymentCompletedLegacy)
                .ThenAsync(ctx => OnPaid(ctx, logger))
                .Publish(ctx => new CommitInventoryCommand(ctx.Saga.OrderId, DateTimeOffset.UtcNow))
                .TransitionTo(Completed),

            When(PaymentFailed)
                .ThenAsync(ctx => BeginCompensation(ctx, logger, PaymentDeclinedReason(ctx.Message.Reason)))
                .Publish(ctx => new ReleaseInventoryCommand(ctx.Saga.OrderId, ctx.Saga.Items, ctx.Message.Reason, DateTimeOffset.UtcNow))
                .TransitionTo(Compensating),

            When(PaymentFailedLegacy)
                .ThenAsync(ctx => BeginCompensation(ctx, logger, PaymentDeclinedReason(ctx.Message.Reason)))
                .Publish(ctx => new ReleaseInventoryCommand(ctx.Saga.OrderId, ctx.Saga.Items, ctx.Message.Reason, DateTimeOffset.UtcNow))
                .TransitionTo(Compensating),

            When(OrderCancelled)
                .Then(ctx => Fail(ctx.Saga, ctx.Message.Reason))
                .Publish(ctx => new ReleaseInventoryCommand(ctx.Saga.OrderId, ctx.Saga.Items, ctx.Message.Reason, DateTimeOffset.UtcNow))
                .TransitionTo(Compensating),

            Ignore(InventoryReserved), Ignore(StockReservedLegacy)
        );

        // ── Compensating ──────────────────────────────────────────────────────────
        During(Compensating,
            When(InventoryReleased)
                .ThenAsync(async ctx =>
                {
                    logger.LogInformation("Saga[{OrderId}] InventoryReleased → Cancelled (compensation complete).", ctx.Saga.OrderId);
                    ctx.Saga.UpdatedAtUtc = DateTime.UtcNow;
                    await UpdateOrder(ctx, o => { if (o.Status != OrderStatus.Cancelled) o.Cancel(ctx.Saga.FailureReason); });
                })
                .TransitionTo(Cancelled),

            // A payment that completes after a cancellation is not reversed automatically here.
            When(PaymentProcessed)
                .Then(ctx => logger.LogWarning("Saga[{OrderId}] payment {Txn} completed after cancellation; refund required.",
                    ctx.Saga.OrderId, ctx.Message.TransactionId)),

            Ignore(PaymentCompletedLegacy), Ignore(PaymentFailed), Ignore(PaymentFailedLegacy), Ignore(OrderCancelled),
            Ignore(InventoryReserved), Ignore(StockReservedLegacy), Ignore(InventoryReservationFailed), Ignore(StockReservationFailedLegacy)
        );

        // ── Terminal states: absorb duplicates / late arrivals ────────────────────
        During(Completed,
            Ignore(PaymentProcessed), Ignore(PaymentCompletedLegacy), Ignore(PaymentFailed), Ignore(PaymentFailedLegacy),
            Ignore(InventoryReserved), Ignore(StockReservedLegacy), Ignore(InventoryReleased), Ignore(OrderCancelled),
            Ignore(InventoryReservationFailed), Ignore(StockReservationFailedLegacy)
        );

        During(Cancelled,
            // A reservation that lands after cancellation must be handed back.
            When(InventoryReserved)
                .Publish(ctx => new ReleaseInventoryCommand(ctx.Saga.OrderId, ctx.Saga.Items, "Order already cancelled", DateTimeOffset.UtcNow)),
            When(StockReservedLegacy)
                .Publish(ctx => new ReleaseInventoryCommand(ctx.Saga.OrderId, ctx.Saga.Items, "Order already cancelled", DateTimeOffset.UtcNow)),
            Ignore(PaymentProcessed), Ignore(PaymentCompletedLegacy), Ignore(PaymentFailed), Ignore(PaymentFailedLegacy),
            Ignore(InventoryReleased), Ignore(OrderCancelled), Ignore(InventoryReservationFailed), Ignore(StockReservationFailedLegacy)
        );

        // Duplicate OrderCreated deliveries after the saga has started are harmless.
        DuringAny(Ignore(OrderCreated), Ignore(OrderCreatedApp));
    }

    public static string PaymentDeclinedReason(string? reason) =>
        string.IsNullOrWhiteSpace(reason) ? "Payment declined" : $"Payment declined: {reason}";

    private static ProcessPaymentCommand BuildPaymentCommand(OrderStateData saga) =>
        new(saga.OrderId,
            Guid.TryParse(saga.CustomerId, out var customerId) ? customerId : Guid.Empty,
            saga.TotalAmount,
            DateTimeOffset.UtcNow)
        {
            PaymentMethod = string.IsNullOrWhiteSpace(saga.PaymentMethod) ? DefaultPaymentMethod : saga.PaymentMethod
        };

    private static void InitializeSaga(
        OrderStateData saga,
        Guid orderId,
        string customerId,
        decimal totalAmount,
        IEnumerable<OrderItemDto>? items,
        DateTime createdAtUtc,
        string? paymentMethod)
    {
        saga.OrderId = orderId;
        saga.CustomerId = customerId ?? string.Empty;
        saga.TotalAmount = totalAmount;
        saga.Items = items?.ToList() ?? new List<OrderItemDto>();
        saga.PaymentMethod = string.IsNullOrWhiteSpace(paymentMethod) ? null : paymentMethod;
        saga.CreatedAtUtc = createdAtUtc;
        saga.UpdatedAtUtc = DateTime.UtcNow;
    }

    private static void Fail(OrderStateData saga, string? reason)
    {
        saga.FailureReason = string.IsNullOrWhiteSpace(reason) ? "Order cancelled" : reason;
        saga.UpdatedAtUtc = DateTime.UtcNow;
    }

    private static async Task OnStockReserved<T>(BehaviorContext<OrderStateData, T> ctx, ILogger logger) where T : class
    {
        logger.LogInformation("Saga[{OrderId}] InventoryReserved → AwaitingPayment (charge {Amount}).", ctx.Saga.OrderId, ctx.Saga.TotalAmount);
        ctx.Saga.UpdatedAtUtc = DateTime.UtcNow;
        await UpdateOrder(ctx, o => { if (o.Status == OrderStatus.Pending) o.MarkStockReserved(); });
    }

    private static async Task OnPaid<T>(BehaviorContext<OrderStateData, T> ctx, ILogger logger) where T : class
    {
        logger.LogInformation("Saga[{OrderId}] PaymentProcessed → Completed (commit stock).", ctx.Saga.OrderId);
        ctx.Saga.UpdatedAtUtc = DateTime.UtcNow;
        await UpdateOrder(ctx, o =>
        {
            if (o.Status == OrderStatus.Cancelled) return;
            o.MarkPaid();
            o.MarkCompleted();
        });
    }

    private static async Task FailWithoutReservation<T>(BehaviorContext<OrderStateData, T> ctx, ILogger logger, string reason) where T : class
    {
        logger.LogWarning("Saga[{OrderId}] InventoryReservationFailed ({Reason}) → Cancelled.", ctx.Saga.OrderId, reason);
        Fail(ctx.Saga, string.IsNullOrWhiteSpace(reason) ? "Out of stock" : $"Out of stock: {reason}");
        await UpdateOrder(ctx, o => { if (o.Status != OrderStatus.Cancelled) o.Cancel(ctx.Saga.FailureReason); });
    }

    private static async Task BeginCompensation<T>(BehaviorContext<OrderStateData, T> ctx, ILogger logger, string reason) where T : class
    {
        logger.LogWarning("Saga[{OrderId}] {Reason} → Compensating (release stock).", ctx.Saga.OrderId, reason);
        Fail(ctx.Saga, reason);
        // The customer-facing order is cancelled immediately; the saga finishes once stock is released.
        await UpdateOrder(ctx, o => { if (o.Status != OrderStatus.Cancelled) o.Cancel(reason); });
    }

    private static OrderingDbContext? ResolveDbContext(BehaviorContext<OrderStateData> context)
    {
        if (context.TryGetPayload<OrderingDbContext>(out var direct))
            return direct;
        if (context.TryGetPayload<IServiceProvider>(out var sp))
            return sp.GetService<OrderingDbContext>();
        try
        {
            return context.GetServiceOrCreateInstance<OrderingDbContext>();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Loads the Order aggregate, applies a transition, and persists it.</summary>
    private static async Task UpdateOrder(BehaviorContext<OrderStateData> context, Action<Order> apply)
    {
        var db = ResolveDbContext(context);
        if (db is null)
        {
            throw new InvalidOperationException("OrderingDbContext is not available in the saga scope.");
        }

        var order = await db.Orders.FindAsync(new object[] { context.Saga.OrderId }, context.CancellationToken);
        if (order is null)
        {
            return;
        }

        apply(order);
        await db.SaveChangesAsync(context.CancellationToken);
    }
}
