using MassTransit;
using ShopSphere.EventBus.Events;
using ShopSphere.Services.Payment.Models;
using ShopSphere.Services.Payment.Services;

namespace ShopSphere.Services.Payment.Consumers;

/// <summary>
/// Saga step 2: charge the order once its stock is reserved. The PaymentService publishes
/// PaymentProcessed / PaymentFailed, which the Ordering saga consumes.
/// </summary>
public class ProcessPaymentConsumer : IConsumer<ProcessPaymentCommand>
{
    private const string DefaultPaymentMethod = "card";

    private readonly IPaymentService _paymentService;
    private readonly ILogger<ProcessPaymentConsumer> _logger;

    public ProcessPaymentConsumer(IPaymentService paymentService, ILogger<ProcessPaymentConsumer> logger)
    {
        _paymentService = paymentService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ProcessPaymentCommand> context)
    {
        var message = context.Message;
        var method = string.IsNullOrWhiteSpace(message.PaymentMethod) ? DefaultPaymentMethod : message.PaymentMethod;
        _logger.LogInformation("ProcessPaymentCommand received for Order {OrderId}: {Amount} via {Method}.",
            message.OrderId, message.Amount, method);

        var (isConflict, response, error) = await _paymentService.ProcessPaymentAsync(
            new ProcessPaymentRequest(message.OrderId, message.Amount, "USD", method),
            context.CancellationToken);

        if (!isConflict)
        {
            _logger.LogInformation("Payment for Order {OrderId} finished: {Status}{Error}.",
                message.OrderId, response?.Status, error is null ? "" : $" ({error})");
            return;
        }

        // Already handled elsewhere (e.g. a direct POST /payments or a redelivery):
        // re-announce the settled outcome so the saga can always progress.
        switch (response?.Status)
        {
            case "Completed":
                await context.Publish(new PaymentProcessedIntegrationEvent(
                    response.Id, response.OrderId, response.Amount, response.TransactionId ?? string.Empty,
                    response.UpdatedAtUtc ?? DateTime.UtcNow));
                break;
            case "Failed":
                await context.Publish(new PaymentFailedIntegrationEvent(
                    response.OrderId, response.FailureReason ?? "Payment failed", response.UpdatedAtUtc ?? DateTime.UtcNow));
                break;
            default:
                // Another worker is mid-flight; retry later.
                throw new InvalidOperationException($"Payment for Order {message.OrderId} is still processing.");
        }
    }
}
