using FluentValidation;
using ShopSphere.Services.Payment.Models;

namespace ShopSphere.Services.Payment.Validators;

public class ProcessPaymentRequestValidator : AbstractValidator<ProcessPaymentRequest>
{
    public ProcessPaymentRequestValidator()
    {
        RuleFor(x => x.OrderId)
            .NotEmpty()
            .WithMessage("OrderId is required and cannot be empty.");

        RuleFor(x => x.Amount)
            .GreaterThan(0)
            .WithMessage("Amount must be greater than zero.");

        RuleFor(x => x.Currency)
            .NotEmpty()
            .WithMessage("Currency is required.")
            .Length(3)
            .WithMessage("Currency must be a 3-character ISO currency code (e.g., USD).");

        RuleFor(x => x.PaymentMethod)
            .NotEmpty()
            .WithMessage("PaymentMethod is required.");
    }
}

