using MassTransit;
using Microsoft.EntityFrameworkCore;
using ShopSphere.EventBus.Events;
using ShopSphere.Services.Payment.Data;
using ShopSphere.Services.Payment.Entities;
using ShopSphere.Services.Payment.Models;

namespace ShopSphere.Services.Payment.Services;

public class PaymentService : IPaymentService
{
    private readonly PaymentDbContext _dbContext;
    private readonly IPaymentProcessor _paymentProcessor;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        PaymentDbContext dbContext,
        IPaymentProcessor paymentProcessor,
        IPublishEndpoint publishEndpoint,
        ILogger<PaymentService> logger)
    {
        _dbContext = dbContext;
        _paymentProcessor = paymentProcessor;
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task<(bool IsConflict, PaymentResponse? Response, string? Error)> ProcessPaymentAsync(
        ProcessPaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing payment for OrderId: {OrderId}", request.OrderId);

        // Check if transaction already exists for this order
        var transaction = await _dbContext.Transactions
            .FirstOrDefaultAsync(t => t.OrderId == request.OrderId, cancellationToken);

        if (transaction != null && transaction.Status == PaymentStatus.Completed)
        {
            _logger.LogWarning("Payment for OrderId {OrderId} has already been completed (Idempotency conflict).", request.OrderId);
            return (IsConflict: true, Response: null, Error: $"Payment for OrderId '{request.OrderId}' has already been completed.");
        }

        if (transaction == null)
        {
            transaction = new PaymentTransaction
            {
                Id = Guid.NewGuid(),
                OrderId = request.OrderId,
                Amount = request.Amount,
                Currency = request.Currency,
                PaymentMethod = request.PaymentMethod,
                Status = PaymentStatus.Processing,
                CreatedAtUtc = DateTime.UtcNow
            };
            _dbContext.Transactions.Add(transaction);
        }
        else
        {
            transaction.Amount = request.Amount;
            transaction.Currency = request.Currency;
            transaction.PaymentMethod = request.PaymentMethod;
            transaction.Status = PaymentStatus.Processing;
            transaction.UpdatedAtUtc = DateTime.UtcNow;
        }

        // Delegate to Stripe / Simulator engine
        var processorResult = await _paymentProcessor.ProcessPaymentAsync(
            request.OrderId,
            request.Amount,
            request.Currency,
            request.PaymentMethod,
            cancellationToken);

        if (processorResult.IsSuccess)
        {
            transaction.Status = PaymentStatus.Completed;
            transaction.TransactionId = processorResult.TransactionId;
            transaction.FailureReason = null;
            transaction.UpdatedAtUtc = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync(cancellationToken);

            // Publish Integration Events to RabbitMQ Bus
            try
            {
                var processedEvent = new PaymentProcessedIntegrationEvent(
                    transaction.Id,
                    transaction.OrderId,
                    transaction.Amount,
                    transaction.TransactionId!,
                    transaction.UpdatedAtUtc.Value);

                await _publishEndpoint.Publish(processedEvent, cancellationToken);

                // Compatible event for Saga Orchestrator
                var completedEvent = new PaymentCompletedEvent(
                    transaction.Id,
                    transaction.OrderId,
                    transaction.Amount,
                    transaction.TransactionId!,
                    DateTimeOffset.UtcNow);

                await _publishEndpoint.Publish(completedEvent, cancellationToken);

                _logger.LogInformation("Published PaymentProcessedIntegrationEvent for OrderId {OrderId}.", request.OrderId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish payment completion events for OrderId {OrderId}.", request.OrderId);
            }

            return (IsConflict: false, Response: ToResponse(transaction), Error: null);
        }
        else
        {
            transaction.Status = PaymentStatus.Failed;
            transaction.FailureReason = processorResult.FailureReason ?? "Payment processing failed.";
            transaction.UpdatedAtUtc = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync(cancellationToken);

            // Publish PaymentFailedIntegrationEvent
            try
            {
                var failedIntegrationEvent = new PaymentFailedIntegrationEvent(
                    transaction.OrderId,
                    transaction.FailureReason!,
                    transaction.UpdatedAtUtc.Value);

                await _publishEndpoint.Publish(failedIntegrationEvent, cancellationToken);

                var failedEvent = new PaymentFailedEvent(
                    transaction.OrderId,
                    transaction.FailureReason!,
                    DateTimeOffset.UtcNow);

                await _publishEndpoint.Publish(failedEvent, cancellationToken);

                _logger.LogInformation("Published PaymentFailedIntegrationEvent for OrderId {OrderId}.", request.OrderId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish payment failure events for OrderId {OrderId}.", request.OrderId);
            }

            return (IsConflict: false, Response: ToResponse(transaction), Error: transaction.FailureReason);
        }
    }

    public async Task<PaymentResponse?> GetPaymentByOrderIdAsync(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var transaction = await _dbContext.Transactions
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.OrderId == orderId, cancellationToken);

        return transaction == null ? null : ToResponse(transaction);
    }

    public async Task<StripeWebhookResponse> HandleWebhookAsync(
        string payload,
        string? signatureHeader,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Handling incoming Stripe webhook...");

        var (success, eventType, transactionId, orderId) = await _paymentProcessor.ParseWebhookAsync(
            payload,
            signatureHeader,
            cancellationToken);

        if (!success)
        {
            _logger.LogWarning("Stripe webhook payload parsing failed.");
            return new StripeWebhookResponse(false, eventType, "Unable to parse Stripe webhook event.");
        }

        PaymentTransaction? transaction = null;

        if (orderId.HasValue)
        {
            transaction = await _dbContext.Transactions
                .FirstOrDefaultAsync(t => t.OrderId == orderId.Value, cancellationToken);
        }

        if (transaction == null && !string.IsNullOrEmpty(transactionId))
        {
            transaction = await _dbContext.Transactions
                .FirstOrDefaultAsync(t => t.TransactionId == transactionId, cancellationToken);
        }

        if (transaction != null)
        {
            if (eventType == "payment_intent.succeeded" || eventType == "charge.succeeded")
            {
                if (transaction.Status != PaymentStatus.Completed)
                {
                    transaction.Status = PaymentStatus.Completed;
                    transaction.TransactionId = transactionId ?? transaction.TransactionId;
                    transaction.UpdatedAtUtc = DateTime.UtcNow;
                    await _dbContext.SaveChangesAsync(cancellationToken);

                    try
                    {
                        await _publishEndpoint.Publish(new PaymentProcessedIntegrationEvent(
                            transaction.Id,
                            transaction.OrderId,
                            transaction.Amount,
                            transaction.TransactionId ?? string.Empty,
                            transaction.UpdatedAtUtc.Value), cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error publishing event after webhook update.");
                    }
                }
            }
            else if (eventType == "payment_intent.payment_failed" || eventType == "charge.failed")
            {
                transaction.Status = PaymentStatus.Failed;
                transaction.FailureReason = "Payment failed according to Stripe webhook.";
                transaction.UpdatedAtUtc = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync(cancellationToken);

                try
                {
                    await _publishEndpoint.Publish(new PaymentFailedIntegrationEvent(
                        transaction.OrderId,
                        transaction.FailureReason,
                        transaction.UpdatedAtUtc.Value), cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error publishing event after webhook failure.");
                }
            }
        }

        return new StripeWebhookResponse(true, eventType, "Stripe webhook processed successfully.");
    }

    private static PaymentResponse ToResponse(PaymentTransaction t) =>
        new(
            t.Id,
            t.OrderId,
            t.Amount,
            t.Currency,
            t.Status.ToString(),
            t.PaymentMethod,
            t.TransactionId,
            t.FailureReason,
            t.CreatedAtUtc,
            t.UpdatedAtUtc
        );
}

