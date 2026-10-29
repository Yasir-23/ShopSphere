using FluentValidation;
using ShopSphere.Services.Basket.Models;

namespace ShopSphere.Services.Basket.Validators;

public class UpdateBasketRequestValidator : AbstractValidator<UpsertBasketRequest>
{
    public UpdateBasketRequestValidator()
    {
        RuleFor(x => x.Items)
            .NotNull().WithMessage("Cart items collection is required.")
            .NotEmpty().WithMessage("Cart must contain at least one item.");

        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductId)
                .NotEmpty().WithMessage("Product ID is required.");

            item.RuleFor(i => i.ProductName)
                .NotEmpty().WithMessage("Product name is required.");

            item.RuleFor(i => i.Quantity)
                .GreaterThan(0).WithMessage("Quantity must be greater than zero.");
        });
    }
}
