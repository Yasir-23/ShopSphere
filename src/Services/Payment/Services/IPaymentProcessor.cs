namespace ShopSphere.Services.Payment.Services;

public record PaymentProcessingResult(
    bool IsSuccess,
    string? TransactionId,
    string? FailureReason
);

public interface IPaymentProcessor
{
    Task<PaymentProcessingResult> ProcessPaymentAsync(
        Guid orderId,
        decimal amount,
        string currency,
        string paymentMethod,
        CancellationToken cancellationToken = default);

    Task<(bool Success, string EventType, string? TransactionId, Guid? OrderId)> ParseWebhookAsync(
        string requestBody,
        string? signatureHeader,
        CancellationToken cancellationToken = default);
}

