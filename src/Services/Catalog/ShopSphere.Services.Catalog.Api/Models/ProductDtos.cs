namespace ShopSphere.Services.Catalog.Api.Models;

public record ProductResponse(
    string Id,
    string Name,
    string Slug,
    string Description,
    string Sku,
    decimal Price,
    string Currency,
    string Category,
    string? CategoryId,
    Dictionary<string, string> Attributes,
    List<string> Images,
    string ImageUrl,
    bool IsAvailable,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public record CategoryResponse(
    string Id,
    string Name,
    string Description);

public record CreateProductRequest(
    string Name,
    string Description,
    string Sku,
    decimal Price,
    string? Currency,
    string Category,
    string? CategoryId,
    Dictionary<string, string>? Attributes,
    List<string>? Images,
    string? ImageUrl,
    bool? IsAvailable);

public record UpdateProductRequest(
    string Name,
    string Description,
    string Sku,
    decimal Price,
    string? Currency,
    string Category,
    string? CategoryId,
    Dictionary<string, string>? Attributes,
    List<string>? Images,
    bool? IsAvailable);

public record ProductFilterParameters(
    int PageNumber = 1,
    int PageSize = 10,
    string? SearchTerm = null,
    string? Category = null,
    string? CategoryId = null,
    decimal? MinPrice = null,
    decimal? MaxPrice = null,
    string? SortBy = null);
