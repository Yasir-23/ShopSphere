using FluentValidation;
using ShopSphere.Services.Inventory.Models;

namespace ShopSphere.Services.Inventory.Validators;

public class ReserveStockRequestValidator : AbstractValidator<ReserveStockRequest>
{
    public ReserveStockRequestValidator()
    {
        RuleFor(x => x.OrderId)
            .NotEmpty()
            .WithMessage("OrderId is required and cannot be empty.");

        RuleFor(x => x.Items)
            .NotEmpty()
            .WithMessage("At least one stock item must be specified for reservation.");

        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductId)
                .NotEmpty()
                .WithMessage("ProductId is required for each reservation item.");

            item.RuleFor(i => i.Quantity)
                .GreaterThan(0)
                .WithMessage("Reservation quantity must be greater than zero.");
        });
    }
}

public class ReleaseStockRequestValidator : AbstractValidator<ReleaseStockRequest>
{
    public ReleaseStockRequestValidator()
    {
        RuleFor(x => x.OrderId)
            .NotEmpty()
            .WithMessage("OrderId is required and cannot be empty.");

        When(x => x.Items != null && x.Items.Count > 0, () =>
        {
            RuleForEach(x => x.Items).ChildRules(item =>
            {
                item.RuleFor(i => i.ProductId)
                    .NotEmpty()
                    .WithMessage("ProductId is required for each item when specifying items for release.");

                item.RuleFor(i => i.Quantity)
                    .GreaterThan(0)
                    .WithMessage("Release quantity must be greater than zero.");
            });
        });
    }
}

public class AdjustStockRequestValidator : AbstractValidator<AdjustStockRequest>
{
    public AdjustStockRequestValidator()
    {
        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.Sku) || !string.IsNullOrWhiteSpace(x.ProductId))
            .WithMessage("Either Sku or ProductId must be provided to adjust inventory.");

        RuleFor(x => x.Quantity)
            .GreaterThan(0)
            .WithMessage("Stock adjustment quantity must be greater than zero.");
    }
}

