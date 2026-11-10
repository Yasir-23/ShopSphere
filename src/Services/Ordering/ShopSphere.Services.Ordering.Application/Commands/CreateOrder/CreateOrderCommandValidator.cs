using FluentValidation;

namespace ShopSphere.Services.Ordering.Application.Commands.CreateOrder;

public class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderCommandValidator()
    {
        RuleFor(c => c.CustomerId)
            .NotEmpty().WithMessage("CustomerId is required.");

        RuleFor(c => c.CustomerEmail)
            .NotEmpty().WithMessage("CustomerEmail is required.")
            .EmailAddress().WithMessage("CustomerEmail must be a valid email address.");

        RuleFor(c => c.ShippingAddress)
            .NotNull().WithMessage("ShippingAddress is required.");

        When(c => c.ShippingAddress != null, () =>
        {
            RuleFor(c => c.ShippingAddress.Street)
                .NotEmpty().WithMessage("Street is required.");

            RuleFor(c => c.ShippingAddress.City)
                .NotEmpty().WithMessage("City is required.");

            RuleFor(c => c.ShippingAddress.State)
                .NotEmpty().WithMessage("State is required.");

            RuleFor(c => c.ShippingAddress.ZipCode)
                .NotEmpty().WithMessage("ZipCode is required.");

            RuleFor(c => c.ShippingAddress.Country)
                .NotEmpty().WithMessage("Country is required.");
        });

        RuleFor(c => c.PaymentMethod)
            .MaximumLength(64).WithMessage("PaymentMethod must not exceed 64 characters.");

        RuleFor(c => c.Items)
            .NotEmpty().WithMessage("At least one item must be included in the order.");

        RuleForEach(c => c.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductId)
                .NotEmpty().WithMessage("ProductId is required.");

            item.RuleFor(i => i.ProductName)
                .NotEmpty().WithMessage("ProductName is required.");

            item.RuleFor(i => i.UnitPrice)
                .GreaterThan(0).WithMessage("UnitPrice must be greater than zero.");

            item.RuleFor(i => i.Quantity)
                .GreaterThan(0).WithMessage("Quantity must be greater than zero.");
        });
    }
}

