namespace ShopSphere.Services.Payment.Models;

public record ProcessPaymentRequest(
    Guid OrderId,
    decimal Amount,
    string Currency,
    string PaymentMethod
);

public record PaymentResponse(
    Guid Id,
    Guid OrderId,
    decimal Amount,
    string Currency,
    string Status,
    string PaymentMethod,
    string? TransactionId,
    string? FailureReason,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc
);

public record StripeWebhookResponse(
    bool Received,
    string? EventType,
    string? Message
);

