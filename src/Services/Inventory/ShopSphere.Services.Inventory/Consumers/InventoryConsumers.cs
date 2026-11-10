using MassTransit;
using ShopSphere.EventBus.Events;
using ShopSphere.Services.Inventory.Models;
using ShopSphere.Services.Inventory.Services;

namespace ShopSphere.Services.Inventory.Consumers;

/// <summary>
/// Saga step 1: reserve stock for every line of a new order (all-or-nothing) and report back.
/// </summary>
public class ReserveInventoryConsumer : IConsumer<ReserveInventoryCommand>
{
    private readonly IInventoryService _inventory;
    private readonly ILogger<ReserveInventoryConsumer> _logger;

    public ReserveInventoryConsumer(IInventoryService inventory, ILogger<ReserveInventoryConsumer> logger)
    {
        _inventory = inventory;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ReserveInventoryCommand> context)
    {
        var message = context.Message;
        _logger.LogInformation("ReserveInventoryCommand received for Order {OrderId} ({Count} lines).",
            message.OrderId, message.Items?.Count ?? 0);

        if (message.Items is null || message.Items.Count == 0)
        {
            await context.Publish(new InventoryReservationFailedIntegrationEvent(
                message.OrderId, "The order has no items to reserve.", DateTime.UtcNow));
            return;
        }

        var result = await _inventory.ReserveStockAsync(
            new ReserveStockRequest(
                message.OrderId,
                message.Items.Select(i => new StockReservationItem(i.ProductId, i.Quantity)).ToList()),
            context.CancellationToken);

        if (result.IsSuccess)
        {
            await context.Publish(new InventoryReservedIntegrationEvent(message.OrderId, DateTime.UtcNow));
            _logger.LogInformation("Published InventoryReservedIntegrationEvent for Order {OrderId}.", message.OrderId);
            return;
        }

        if (result.Status == "ConcurrencyConflict")
        {
            // Transient: let MassTransit's retry policy redeliver.
            throw new InvalidOperationException(result.ErrorMessage);
        }

        await context.Publish(new InventoryReservationFailedIntegrationEvent(
            message.OrderId, result.ErrorMessage ?? "Stock could not be reserved.", DateTime.UtcNow));
        _logger.LogWarning("Published InventoryReservationFailedIntegrationEvent for Order {OrderId}: {Reason}",
            message.OrderId, result.ErrorMessage);
    }
}

/// <summary>
/// Saga compensation: hand reserved stock back after a payment failure or cancellation.
/// Idempotent, and always acknowledged so the saga can finish.
/// </summary>
public class ReleaseInventoryConsumer : IConsumer<ReleaseInventoryCommand>
{
    private readonly IInventoryService _inventory;
    private readonly ILogger<ReleaseInventoryConsumer> _logger;

    public ReleaseInventoryConsumer(IInventoryService inventory, ILogger<ReleaseInventoryConsumer> logger)
    {
        _inventory = inventory;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ReleaseInventoryCommand> context)
    {
        var message = context.Message;
        _logger.LogInformation("ReleaseInventoryCommand received for Order {OrderId}: {Reason}", message.OrderId, message.Reason);

        // Items are deliberately not passed: only reservations recorded for this order are released,
        // so a duplicate or premature release can never touch other orders' stock.
        var result = await _inventory.ReleaseStockAsync(new ReleaseStockRequest(message.OrderId), context.CancellationToken);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Releasing stock for Order {message.OrderId} failed: {result.ErrorMessage}");
        }

        await context.Publish(new InventoryReleasedIntegrationEvent(message.OrderId, DateTime.UtcNow));
        _logger.LogInformation("Published InventoryReleasedIntegrationEvent for Order {OrderId}.", message.OrderId);
    }
}

/// <summary>
/// Saga completion: convert the order's reservation into a permanent stock deduction.
/// </summary>
public class CommitInventoryConsumer : IConsumer<CommitInventoryCommand>
{
    private readonly IInventoryService _inventory;
    private readonly ILogger<CommitInventoryConsumer> _logger;

    public CommitInventoryConsumer(IInventoryService inventory, ILogger<CommitInventoryConsumer> logger)
    {
        _inventory = inventory;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<CommitInventoryCommand> context)
    {
        var orderId = context.Message.OrderId;
        _logger.LogInformation("CommitInventoryCommand received for Order {OrderId}.", orderId);

        var result = await _inventory.CommitStockAsync(new CommitStockRequest(orderId), context.CancellationToken);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Committing stock for Order {orderId} failed: {result.ErrorMessage}");
        }
    }
}
