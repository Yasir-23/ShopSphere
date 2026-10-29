using FluentValidation;
using ShopSphere.Services.Basket.Models;

namespace ShopSphere.Services.Basket.Validators;

public class CheckoutBasketRequestValidator : AbstractValidator<CheckoutBasketRequest>
{
    public CheckoutBasketRequestValidator()
    {
        RuleFor(x => x.ShippingAddress)
            .NotNull().WithMessage("Shipping address is required.");

        When(x => x.ShippingAddress != null, () =>
        {
            RuleFor(x => x.ShippingAddress.Street)
                .NotEmpty().WithMessage("Street is required.");

            RuleFor(x => x.ShippingAddress.City)
                .NotEmpty().WithMessage("City is required.");

            RuleFor(x => x.ShippingAddress.State)
                .NotEmpty().WithMessage("State is required.");

            RuleFor(x => x.ShippingAddress.ZipCode)
                .NotEmpty().WithMessage("Zip code is required.");

            RuleFor(x => x.ShippingAddress.Country)
                .NotEmpty().WithMessage("Country is required.");
        });
    }
}
