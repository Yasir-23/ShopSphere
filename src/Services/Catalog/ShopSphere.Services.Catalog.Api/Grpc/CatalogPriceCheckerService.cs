using Grpc.Core;
using MongoDB.Driver;
using ShopSphere.Services.Catalog.Api.Data;
using ShopSphere.Services.Catalog.Api.Models;
using ShopSphere.Services.Catalog.Grpc;

namespace ShopSphere.Services.Catalog.Api.Grpc;

public class CatalogPriceCheckerService : CatalogPriceChecker.CatalogPriceCheckerBase
{
    private readonly ICatalogDbContext _context;
    private readonly ILogger<CatalogPriceCheckerService> _logger;

    public CatalogPriceCheckerService(
        ICatalogDbContext context,
        ILogger<CatalogPriceCheckerService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public override async Task<GetItemPricesResponse> GetItemPrices(
        GetItemPricesRequest request, 
        ServerCallContext context)
    {
        _logger.LogInformation("Processing gRPC GetItemPrices request for {Count} items.", request.ProductIds.Count);

        var response = new GetItemPricesResponse();
        if (request.ProductIds.Count == 0)
        {
            return response;
        }

        var uniqueIds = request.ProductIds.Distinct().ToList();

        // 1. Query by Id
        var idFilter = Builders<Product>.Filter.In(p => p.Id, uniqueIds);
        var products = await _context.Products
            .Find(idFilter)
            .ToListAsync(context.CancellationToken);

        // 2. Fallback check by SKU if any ids were not matched
        if (products.Count < uniqueIds.Count)
        {
            var foundIds = products.Select(p => p.Id).ToHashSet();
            var remainingIds = uniqueIds.Where(id => !foundIds.Contains(id)).ToList();

            if (remainingIds.Count > 0)
            {
                var skuFilter = Builders<Product>.Filter.In(p => p.Sku, remainingIds);
                var skuProducts = await _context.Products
                    .Find(skuFilter)
                    .ToListAsync(context.CancellationToken);

                products.AddRange(skuProducts);
            }
        }

        foreach (var product in products)
        {
            response.Items.Add(new ProductPriceItem
            {
                ProductId = product.Id,
                Price = (double)product.Price,
                Currency = product.Currency,
                IsAvailable = product.IsAvailable
            });
        }

        _logger.LogInformation("Found and returned {Count} product prices via gRPC.", response.Items.Count);
        return response;
    }
}
