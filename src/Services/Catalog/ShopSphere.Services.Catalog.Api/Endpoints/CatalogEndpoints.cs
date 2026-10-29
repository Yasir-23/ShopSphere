using System.Text.RegularExpressions;
using Carter;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using ShopSphere.Services.Catalog.Api.Data;
using ShopSphere.Services.Catalog.Api.Models;

namespace ShopSphere.Services.Catalog.Api.Endpoints;

public class CatalogEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/catalog")
            .WithTags("Catalog");

        // 1. Paginated Product Query
        group.MapGet("/products", async (
            [AsParameters] ProductFilterParameters filter,
            ICatalogDbContext context,
            CancellationToken cancellationToken) =>
        {
            var pageNumber = filter.PageNumber < 1 ? 1 : filter.PageNumber;
            var pageSize = filter.PageSize < 1 ? 10 : (filter.PageSize > 100 ? 100 : filter.PageSize);

            var filterBuilder = Builders<Product>.Filter;
            var filters = new List<FilterDefinition<Product>>();

            // Search term (name or description)
            if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
            {
                var term = filter.SearchTerm.Trim();
                var regex = new BsonRegularExpression(Regex.Escape(term), "i");
                filters.Add(filterBuilder.Or(
                    filterBuilder.Regex(p => p.Name, regex),
                    filterBuilder.Regex(p => p.Description, regex),
                    filterBuilder.Regex(p => p.Sku, regex)
                ));
            }

            // Category filter
            if (!string.IsNullOrWhiteSpace(filter.Category))
            {
                var catRegex = new BsonRegularExpression($"^{Regex.Escape(filter.Category.Trim())}$", "i");
                filters.Add(filterBuilder.Regex(p => p.Category, catRegex));
            }

            // CategoryId filter
            if (!string.IsNullOrWhiteSpace(filter.CategoryId))
            {
                filters.Add(filterBuilder.Eq(p => p.CategoryId, filter.CategoryId.Trim()));
            }

            // Price bounds
            if (filter.MinPrice.HasValue)
            {
                filters.Add(filterBuilder.Gte(p => p.Price, filter.MinPrice.Value));
            }

            if (filter.MaxPrice.HasValue)
            {
                filters.Add(filterBuilder.Lte(p => p.Price, filter.MaxPrice.Value));
            }

            var combinedFilter = filters.Count > 0 ? filterBuilder.And(filters) : filterBuilder.Empty;

            // Sorting
            var sortBuilder = Builders<Product>.Sort;
            var sort = filter.SortBy?.ToLowerInvariant() switch
            {
                "price_asc" => sortBuilder.Ascending(p => p.Price),
                "price_desc" => sortBuilder.Descending(p => p.Price),
                "name" or "name_asc" => sortBuilder.Ascending(p => p.Name),
                "name_desc" => sortBuilder.Descending(p => p.Name),
                "newest" => sortBuilder.Descending(p => p.CreatedAtUtc),
                _ => sortBuilder.Descending(p => p.CreatedAtUtc)
            };

            var totalCount = await context.Products.CountDocumentsAsync(combinedFilter, cancellationToken: cancellationToken);

            var products = await context.Products
                .Find(combinedFilter)
                .Sort(sort)
                .Skip((pageNumber - 1) * pageSize)
                .Limit(pageSize)
                .ToListAsync(cancellationToken);

            var items = products.Select(ToResponse).ToList();
            var result = new PaginatedResult<ProductResponse>(items, totalCount, pageNumber, pageSize);

            return Results.Ok(result);
        })
        .WithName("GetProducts")
        .Produces<PaginatedResult<ProductResponse>>(StatusCodes.Status200OK);

        // 2. Get Product by ID
        group.MapGet("/products/{id}", async (
            string id,
            ICatalogDbContext context,
            CancellationToken cancellationToken) =>
        {
            var product = await context.Products
                .Find(p => p.Id == id)
                .FirstOrDefaultAsync(cancellationToken);

            if (product is null)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Product Not Found",
                    detail: $"Product with ID '{id}' was not found.",
                    instance: $"/api/v1/catalog/products/{id}");
            }

            return Results.Ok(ToResponse(product));
        })
        .WithName("GetProductById")
        .Produces<ProductResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // 3. Create Product (Requires Admin Role)
        group.MapPost("/products", async (
            [FromBody] CreateProductRequest request,
            IValidator<CreateProductRequest> validator,
            ICatalogDbContext context,
            CancellationToken cancellationToken) =>
        {
            var validationResult = await validator.ValidateAsync(request, cancellationToken);
            if (!validationResult.IsValid)
            {
                return Results.ValidationProblem(validationResult.ToDictionary());
            }

            var slug = GenerateSlug(request.Name);
            var now = DateTime.UtcNow;

            var images = request.Images ?? new List<string>();
            if (!string.IsNullOrWhiteSpace(request.ImageUrl) && !images.Contains(request.ImageUrl))
            {
                images.Insert(0, request.ImageUrl);
            }

            var product = new Product
            {
                Id = ObjectId.GenerateNewId().ToString(),
                Name = request.Name.Trim(),
                Slug = slug,
                Description = request.Description.Trim(),
                Sku = request.Sku.Trim().ToUpperInvariant(),
                Price = request.Price,
                Currency = string.IsNullOrWhiteSpace(request.Currency) ? "USD" : request.Currency.Trim().ToUpperInvariant(),
                Category = request.Category.Trim(),
                CategoryId = request.CategoryId?.Trim(),
                Attributes = request.Attributes ?? new Dictionary<string, string>(),
                Images = images,
                IsAvailable = request.IsAvailable ?? true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            await context.Products.InsertOneAsync(product, cancellationToken: cancellationToken);

            var response = ToResponse(product);
            return Results.Created($"/api/v1/catalog/products/{product.Id}", response);
        })
        .RequireAuthorization(policy => policy.RequireRole("Administrator", "Admin"))
        .WithName("CreateProduct")
        .Produces<ProductResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        // 4. Update Product (Requires Admin Role)
        group.MapPut("/products/{id}", async (
            string id,
            [FromBody] UpdateProductRequest request,
            IValidator<UpdateProductRequest> validator,
            ICatalogDbContext context,
            CancellationToken cancellationToken) =>
        {
            var validationResult = await validator.ValidateAsync(request, cancellationToken);
            if (!validationResult.IsValid)
            {
                return Results.ValidationProblem(validationResult.ToDictionary());
            }

            var existingProduct = await context.Products
                .Find(p => p.Id == id)
                .FirstOrDefaultAsync(cancellationToken);

            if (existingProduct is null)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Product Not Found",
                    detail: $"Product with ID '{id}' was not found.",
                    instance: $"/api/v1/catalog/products/{id}");
            }

            // Check SKU uniqueness if changed
            var targetSku = request.Sku.Trim().ToUpperInvariant();
            if (!string.Equals(existingProduct.Sku, targetSku, StringComparison.OrdinalIgnoreCase))
            {
                var skuExists = await context.Products.CountDocumentsAsync(
                    p => p.Sku == targetSku && p.Id != id, 
                    cancellationToken: cancellationToken);

                if (skuExists > 0)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        { "sku", new[] { "A product with this SKU already exists." } }
                    });
                }
            }

            existingProduct.Name = request.Name.Trim();
            existingProduct.Slug = GenerateSlug(request.Name);
            existingProduct.Description = request.Description.Trim();
            existingProduct.Sku = targetSku;
            existingProduct.Price = request.Price;
            existingProduct.Currency = string.IsNullOrWhiteSpace(request.Currency) ? "USD" : request.Currency.Trim().ToUpperInvariant();
            existingProduct.Category = request.Category.Trim();
            existingProduct.CategoryId = request.CategoryId?.Trim() ?? existingProduct.CategoryId;
            existingProduct.Attributes = request.Attributes ?? existingProduct.Attributes;
            existingProduct.Images = request.Images ?? existingProduct.Images;
            if (request.IsAvailable.HasValue)
            {
                existingProduct.IsAvailable = request.IsAvailable.Value;
            }
            existingProduct.UpdatedAtUtc = DateTime.UtcNow;

            await context.Products.ReplaceOneAsync(
                p => p.Id == id, 
                existingProduct, 
                cancellationToken: cancellationToken);

            return Results.Ok(ToResponse(existingProduct));
        })
        .RequireAuthorization(policy => policy.RequireRole("Administrator", "Admin"))
        .WithName("UpdateProduct")
        .Produces<ProductResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        // 5. Delete Product (Requires Admin Role)
        group.MapDelete("/products/{id}", async (
            string id,
            ICatalogDbContext context,
            CancellationToken cancellationToken) =>
        {
            var product = await context.Products
                .Find(p => p.Id == id)
                .FirstOrDefaultAsync(cancellationToken);

            if (product is null)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Product Not Found",
                    detail: $"Product with ID '{id}' was not found.",
                    instance: $"/api/v1/catalog/products/{id}");
            }

            // Soft-delete: mark IsAvailable = false, or remove document
            var deleteResult = await context.Products.DeleteOneAsync(p => p.Id == id, cancellationToken);
            if (deleteResult.DeletedCount == 0)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Product Not Found",
                    detail: $"Product with ID '{id}' could not be deleted.",
                    instance: $"/api/v1/catalog/products/{id}");
            }

            return Results.NoContent();
        })
        .RequireAuthorization(policy => policy.RequireRole("Administrator", "Admin"))
        .WithName("DeleteProduct")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        // 6. List Categories
        group.MapGet("/categories", async (
            ICatalogDbContext context,
            CancellationToken cancellationToken) =>
        {
            var categories = await context.Categories
                .Find(FilterDefinition<Category>.Empty)
                .ToListAsync(cancellationToken);

            var responses = categories.Select(c => new CategoryResponse(c.Id, c.Name, c.Description)).ToList();
            return Results.Ok(responses);
        })
        .WithName("GetCategories")
        .Produces<List<CategoryResponse>>(StatusCodes.Status200OK);
    }

    private static ProductResponse ToResponse(Product p) =>
        new(
            Id: p.Id,
            Name: p.Name,
            Slug: p.Slug,
            Description: p.Description,
            Sku: p.Sku,
            Price: p.Price,
            Currency: p.Currency,
            Category: p.Category,
            CategoryId: p.CategoryId,
            Attributes: p.Attributes ?? new Dictionary<string, string>(),
            Images: p.Images ?? new List<string>(),
            ImageUrl: p.ImageUrl,
            IsAvailable: p.IsAvailable,
            IsActive: p.IsActive,
            CreatedAtUtc: p.CreatedAtUtc,
            UpdatedAtUtc: p.UpdatedAtUtc);

    private static string GenerateSlug(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;

        var slug = name.Trim().ToLowerInvariant();
        slug = Regex.Replace(slug, @"[^a-z0-9\s-]", "");
        slug = Regex.Replace(slug, @"\s+", "-").Trim('-');
        return slug;
    }
}
