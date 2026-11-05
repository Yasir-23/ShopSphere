using System.Text.Json;
using Stripe;

namespace ShopSphere.Services.Payment.Services;

public class StripePaymentProcessor : IPaymentProcessor
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<StripePaymentProcessor> _logger;
    private readonly bool _useMockProvider;
    private readonly string _apiKey;
    private readonly string _webhookSecret;

    public StripePaymentProcessor(
        IConfiguration configuration,
        ILogger<StripePaymentProcessor> logger)
    {
        _configuration = configuration;
        _logger = logger;

        _useMockProvider = _configuration.GetValue<bool>("Payment:UseMockProvider", true);
        _apiKey = _configuration["Stripe:ApiKey"] ?? "sk_test_mock_stripe_key_shopsphere_2026";
        _webhookSecret = _configuration["Stripe:WebhookSecret"] ?? "whsec_mock_stripe_webhook_secret_2026";

        if (!string.IsNullOrEmpty(_apiKey) && !_apiKey.StartsWith("sk_test_mock"))
        {
            StripeConfiguration.ApiKey = _apiKey;
        }
    }

    public async Task<PaymentProcessingResult> ProcessPaymentAsync(
        Guid orderId,
        decimal amount,
        string currency,
        string paymentMethod,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing payment for OrderId: {OrderId}, Amount: {Amount} {Currency}, Method: {PaymentMethod}",
            orderId, amount, currency, paymentMethod);

        // 1. Amount boundary check
        if (amount <= 0)
        {
            _logger.LogWarning("Payment rejected for OrderId {OrderId}: Amount must be positive.", orderId);
            return new PaymentProcessingResult(false, null, "Amount must be greater than zero.");
        }

        // 2. Deterministic test double simulation (SDD Section 4.7 / PRD Epic 6)
        if (_useMockProvider || _apiKey.StartsWith("sk_test_mock"))
        {
            // Simulate brief gateway latency
            await Task.Delay(50, cancellationToken);

            // Card ending in 0000 -> Hard decline
            if (paymentMethod.Contains("0000"))
            {
                _logger.LogWarning("Simulated hard decline for OrderId {OrderId} (Card ending in 0000).", orderId);
                return new PaymentProcessingResult(false, null, "Card Declined");
            }

            // Card ending in 9999 -> Gateway timeout simulation
            if (paymentMethod.Contains("9999"))
            {
                _logger.LogWarning("Simulated timeout for OrderId {OrderId} (Card ending in 9999).", orderId);
                return new PaymentProcessingResult(false, null, "Gateway Timeout");
            }

            // Valid payment simulation
            var transactionId = $"ch_sim_{Guid.NewGuid():N}"[..24];
            _logger.LogInformation("Simulated payment approved for OrderId {OrderId}, TransactionId: {TransactionId}",
                orderId, transactionId);
            return new PaymentProcessingResult(true, transactionId, null);
        }

        // 3. Live Stripe Payment Intent API
        try
        {
            var options = new PaymentIntentCreateOptions
            {
                Amount = (long)(amount * 100), // Convert to cents
                Currency = currency.ToLowerInvariant(),
                PaymentMethod = paymentMethod.StartsWith("pm_") ? paymentMethod : null,
                Confirm = paymentMethod.StartsWith("pm_"),
                Metadata = new Dictionary<string, string>
                {
                    { "orderId", orderId.ToString() }
                }
            };

            var service = new PaymentIntentService();
            var paymentIntent = await service.CreateAsync(options, cancellationToken: cancellationToken);

            if (paymentIntent.Status == "succeeded" || paymentIntent.Status == "requires_capture")
            {
                return new PaymentProcessingResult(true, paymentIntent.Id, null);
            }

            return new PaymentProcessingResult(false, paymentIntent.Id, $"Stripe payment status: {paymentIntent.Status}");
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe exception processing payment for OrderId {OrderId}", orderId);
            return new PaymentProcessingResult(false, null, ex.StripeError?.Message ?? ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error processing payment for OrderId {OrderId}", orderId);
            return new PaymentProcessingResult(false, null, ex.Message);
        }
    }

    public Task<(bool Success, string EventType, string? TransactionId, Guid? OrderId)> ParseWebhookAsync(
        string requestBody,
        string? signatureHeader,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // If real Stripe secret and signature are provided, verify signature
            if (!_useMockProvider &&
                !string.IsNullOrEmpty(signatureHeader) &&
                !string.IsNullOrEmpty(_webhookSecret) &&
                !_webhookSecret.StartsWith("whsec_mock"))
            {
                var stripeEvent = EventUtility.ConstructEvent(requestBody, signatureHeader, _webhookSecret);
                var (txnId, ordId) = ExtractDetailsFromEvent(stripeEvent);
                return Task.FromResult((true, stripeEvent.Type, txnId, ordId));
            }

            // Mock / JSON payload parsing
            using var doc = JsonDocument.Parse(requestBody);
            var root = doc.RootElement;

            string eventType = "payment_intent.succeeded";
            if (root.TryGetProperty("type", out var typeProp))
            {
                eventType = typeProp.GetString() ?? eventType;
            }

            string? transactionId = null;
            Guid? orderId = null;

            if (root.TryGetProperty("data", out var dataProp) &&
                dataProp.TryGetProperty("object", out var objProp))
            {
                if (objProp.TryGetProperty("id", out var idProp))
                {
                    transactionId = idProp.GetString();
                }

                if (objProp.TryGetProperty("metadata", out var metaProp) &&
                    metaProp.TryGetProperty("orderId", out var orderIdProp) &&
                    Guid.TryParse(orderIdProp.GetString(), out var parsedOrderId))
                {
                    orderId = parsedOrderId;
                }
            }

            return Task.FromResult((true, eventType, transactionId, orderId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing Stripe webhook payload.");
            return Task.FromResult((false, string.Empty, (string?)null, (Guid?)null));
        }
    }

    private static (string? TransactionId, Guid? OrderId) ExtractDetailsFromEvent(Event stripeEvent)
    {
        string? transactionId = null;
        Guid? orderId = null;

        if (stripeEvent.Data.Object is PaymentIntent intent)
        {
            transactionId = intent.Id;
            if (intent.Metadata != null &&
                intent.Metadata.TryGetValue("orderId", out var ordStr) &&
                Guid.TryParse(ordStr, out var parsedGuid))
            {
                orderId = parsedGuid;
            }
        }
        else if (stripeEvent.Data.Object is Charge charge)
        {
            transactionId = charge.Id;
            if (charge.Metadata != null &&
                charge.Metadata.TryGetValue("orderId", out var ordStr) &&
                Guid.TryParse(ordStr, out var parsedGuid))
            {
                orderId = parsedGuid;
            }
        }

        return (transactionId, orderId);
    }
}

