using Carter;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using ShopSphere.Services.Payment.Models;
using ShopSphere.Services.Payment.Services;

namespace ShopSphere.Services.Payment.Endpoints;

public class PaymentEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/payments")
            .WithTags("Payments");

        group.MapPost("/", async (
            [FromBody] ProcessPaymentRequest request,
            IValidator<ProcessPaymentRequest> validator,
            IPaymentService paymentService,
            CancellationToken cancellationToken) =>
        {
            var validationResult = await validator.ValidateAsync(request, cancellationToken);
            if (!validationResult.IsValid)
            {
                return Results.ValidationProblem(validationResult.ToDictionary());
            }

            var (isConflict, response, error) = await paymentService.ProcessPaymentAsync(request, cancellationToken);

            if (isConflict)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Payment Conflict",
                    detail: error ?? "Payment for this order has already been completed.",
                    type: "https://shopsphere.local/errors/conflict");
            }

            if (!string.IsNullOrEmpty(error) || response?.Status == "Failed")
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Payment Failed",
                    detail: error ?? "Payment processing failed.",
                    type: "https://shopsphere.local/errors/payment-failed");
            }

            return Results.Ok(response);
        })
        .RequireAuthorization()
        .WithName("ProcessPayment")
        .WithSummary("Process an order payment via Stripe or Simulator")
        .Produces<PaymentResponse>(StatusCodes.Status200OK)
        .Produces<ValidationProblemDetails>(StatusCodes.Status400BadRequest)
        .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces<ProblemDetails>(StatusCodes.Status409Conflict);

        group.MapGet("/{orderId:guid}", async (
            [FromRoute] Guid orderId,
            IPaymentService paymentService,
            CancellationToken cancellationToken) =>
        {
            var payment = await paymentService.GetPaymentByOrderIdAsync(orderId, cancellationToken);
            if (payment == null)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Payment Not Found",
                    detail: $"Payment details for order '{orderId}' were not found.",
                    type: "https://shopsphere.local/errors/not-found");
            }

            return Results.Ok(payment);
        })
        .RequireAuthorization()
        .WithName("GetPaymentByOrderId")
        .WithSummary("Retrieve payment details and receipt by OrderId")
        .Produces<PaymentResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        group.MapPost("/webhook", async (
            HttpRequest httpRequest,
            IPaymentService paymentService,
            CancellationToken cancellationToken) =>
        {
            using var reader = new StreamReader(httpRequest.Body);
            var payload = await reader.ReadToEndAsync(cancellationToken);
            var signatureHeader = httpRequest.Headers["Stripe-Signature"].ToString();

            var result = await paymentService.HandleWebhookAsync(payload, signatureHeader, cancellationToken);
            if (!result.Received)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Webhook Error",
                    detail: result.Message ?? "Failed to process webhook event.",
                    type: "https://shopsphere.local/errors/webhook-error");
            }

            return Results.Ok(result);
        })
        .AllowAnonymous()
        .WithName("HandleStripeWebhook")
        .WithSummary("Stripe Webhook handler accepting mock or test Stripe events")
        .Produces<StripeWebhookResponse>(StatusCodes.Status200OK)
        .Produces<ProblemDetails>(StatusCodes.Status400BadRequest);
    }
}

