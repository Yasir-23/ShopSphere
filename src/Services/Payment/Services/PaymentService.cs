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

        var (transaction, existing) = await ClaimTransactionAsync(request, cancellationToken);
        if (transaction is null)
        {
            var current = existing!;
            var message = current.Status switch
            {
                PaymentStatus.Completed => $"Payment for OrderId '{request.OrderId}' has already been completed.",
                PaymentStatus.Failed => $"Payment for OrderId '{request.OrderId}' was already declined ({current.FailureReason}). Place a new order to retry.",
                PaymentStatus.Refunded => $"Payment for OrderId '{request.OrderId}' has been refunded.",
                _ => $"Payment for OrderId '{request.OrderId}' is already being processed."
            };
            _logger.LogWarning("Payment for OrderId {OrderId} not processed (idempotency): current status {Status}.", request.OrderId, current.Status);
            return (IsConflict: true, Response: ToResponse(current), Error: message);
        }

        // Delegate to Stripe / Simulator engine. Only the request that claimed the transaction gets here.
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

            try
            {
                await _publishEndpoint.Publish(new PaymentProcessedIntegrationEvent(
                    transaction.Id,
                    transaction.OrderId,
                    transaction.Amount,
                    transaction.TransactionId!,
                    transaction.UpdatedAtUtc.Value), cancellationToken);
                _logger.LogInformation("Published PaymentProcessedIntegrationEvent for OrderId {OrderId}.", request.OrderId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish payment completion event for OrderId {OrderId}.", request.OrderId);
            }

            return (IsConflict: false, Response: ToResponse(transaction), Error: null);
        }

        transaction.Status = PaymentStatus.Failed;
        transaction.FailureReason = processorResult.FailureReason ?? "Payment processing failed.";
        transaction.UpdatedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            await _publishEndpoint.Publish(new PaymentFailedIntegrationEvent(
                transaction.OrderId,
                transaction.FailureReason!,
                transaction.UpdatedAtUtc.Value), cancellationToken);
            _logger.LogInformation("Published PaymentFailedIntegrationEvent for OrderId {OrderId}.", request.OrderId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish payment failure event for OrderId {OrderId}.", request.OrderId);
        }

        return (IsConflict: false, Response: ToResponse(transaction), Error: transaction.FailureReason);
    }

    /// <summary>
    /// Ensures exactly one caller processes a payment for an order. The transaction row is created
    /// if missing (tolerating the unique-index race with OrderCreatedConsumer), then atomically
    /// moved Pending → Processing. Returns the claimed, tracked transaction, or the current row
    /// when another request already claimed/finished it.
    /// </summary>
    private async Task<(PaymentTransaction? Claimed, PaymentTransaction? Existing)> ClaimTransactionAsync(
        ProcessPaymentRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await _dbContext.Transactions
            .FirstOrDefaultAsync(t => t.OrderId == request.OrderId, cancellationToken);

        if (existing is null)
        {
            var created = new PaymentTransaction
            {
                Id = Guid.NewGuid(),
                OrderId = request.OrderId,
                Amount = request.Amount,
                Currency = request.Currency,
                PaymentMethod = request.PaymentMethod,
                Status = PaymentStatus.Processing,
                CreatedAtUtc = DateTime.UtcNow
            };
            _dbContext.Transactions.Add(created);
            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                return (created, null);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                _logger.LogInformation("Transaction for OrderId {OrderId} was created concurrently; claiming the existing row.", request.OrderId);
                _dbContext.ChangeTracker.Clear();
                existing = await _dbContext.Transactions
                    .FirstAsync(t => t.OrderId == request.OrderId, cancellationToken);
            }
        }

        if (existing.Status != PaymentStatus.Pending)
        {
            return (null, existing);
        }

        var now = DateTime.UtcNow;
        int claimed;
        if (_dbContext.Database.IsRelational())
        {
            claimed = await _dbContext.Transactions
                .Where(t => t.Id == existing.Id && t.Status == PaymentStatus.Pending)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(t => t.Status, PaymentStatus.Processing)
                    .SetProperty(t => t.Amount, request.Amount)
                    .SetProperty(t => t.Currency, request.Currency)
                    .SetProperty(t => t.PaymentMethod, request.PaymentMethod)
                    .SetProperty(t => t.UpdatedAtUtc, now), cancellationToken);
        }
        else
        {
            // Non-relational providers (in-memory tests) have no set-based update.
            existing.Status = PaymentStatus.Processing;
            existing.Amount = request.Amount;
            existing.Currency = request.Currency;
            existing.PaymentMethod = request.PaymentMethod;
            existing.UpdatedAtUtc = now;
            await _dbContext.SaveChangesAsync(cancellationToken);
            claimed = 1;
        }

        _dbContext.ChangeTracker.Clear();
        var current = await _dbContext.Transactions.FirstAsync(t => t.Id == existing.Id, cancellationToken);
        return claimed == 1 ? (current, null) : (null, current);
    }

    internal static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation };

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

