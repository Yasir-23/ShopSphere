using ShopSphere.Services.Payment.Models;

namespace ShopSphere.Services.Payment.Services;

public interface IPaymentService
{
    Task<(bool IsConflict, PaymentResponse? Response, string? Error)> ProcessPaymentAsync(
        ProcessPaymentRequest request,
        CancellationToken cancellationToken = default);

    Task<PaymentResponse?> GetPaymentByOrderIdAsync(
        Guid orderId,
        CancellationToken cancellationToken = default);

    Task<StripeWebhookResponse> HandleWebhookAsync(
        string payload,
        string? signatureHeader,
        CancellationToken cancellationToken = default);
}

