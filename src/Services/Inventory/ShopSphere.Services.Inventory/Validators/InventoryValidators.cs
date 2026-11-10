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

        // Positive values add stock, negative values remove it (never below the reserved quantity).
        RuleFor(x => x.Quantity)
            .NotEqual(0)
            .WithMessage("Stock adjustment quantity must not be zero.")
            .InclusiveBetween(-1_000_000, 1_000_000)
            .WithMessage("Stock adjustment quantity is out of range.");
    }
}

public class CreateStockItemRequestValidator : AbstractValidator<CreateStockItemRequest>
{
    public CreateStockItemRequestValidator()
    {
        RuleFor(x => x.ProductId)
            .NotEmpty().WithMessage("ProductId is required.")
            .MaximumLength(64).WithMessage("ProductId must not exceed 64 characters.");

        RuleFor(x => x.Sku)
            .NotEmpty().WithMessage("SKU is required.")
            .MaximumLength(64).WithMessage("SKU must not exceed 64 characters.");

        RuleFor(x => x.Quantity)
            .InclusiveBetween(0, 1_000_000).WithMessage("Initial quantity must be between 0 and 1,000,000.");

        RuleFor(x => x.RestockThreshold)
            .InclusiveBetween(0, 1_000_000).When(x => x.RestockThreshold.HasValue)
            .WithMessage("Restock threshold must be between 0 and 1,000,000.");
    }
}

