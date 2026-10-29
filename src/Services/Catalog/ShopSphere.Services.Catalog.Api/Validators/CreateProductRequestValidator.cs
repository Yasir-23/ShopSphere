using FluentValidation;
using MongoDB.Driver;
using ShopSphere.Services.Catalog.Api.Data;
using ShopSphere.Services.Catalog.Api.Models;

namespace ShopSphere.Services.Catalog.Api.Validators;

public class CreateProductRequestValidator : AbstractValidator<CreateProductRequest>
{
    private readonly ICatalogDbContext _context;

    public CreateProductRequestValidator(ICatalogDbContext context)
    {
        _context = context;

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Product name is required.")
            .MaximumLength(200).WithMessage("Product name must not exceed 200 characters.");

        RuleFor(x => x.Description)
            .NotEmpty().WithMessage("Product description is required.");

        RuleFor(x => x.Sku)
            .NotEmpty().WithMessage("SKU is required.")
            .MaximumLength(50).WithMessage("SKU must not exceed 50 characters.")
            .MustAsync(BeUniqueSkuAsync).WithMessage("A product with this SKU already exists.");

        RuleFor(x => x.Price)
            .GreaterThan(0).WithMessage("Price must be greater than zero.");

        RuleFor(x => x.Category)
            .NotEmpty().WithMessage("Category is required.");
    }

    private async Task<bool> BeUniqueSkuAsync(string sku, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sku)) return true;

        var count = await _context.Products.CountDocumentsAsync(
            p => p.Sku == sku, 
            cancellationToken: cancellationToken);

        return count == 0;
    }
}
