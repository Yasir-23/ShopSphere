using ShopSphere.Services.Catalog.Grpc;

namespace ShopSphere.Services.Basket.Services;

public interface IPriceValidationService
{
    Task<Dictionary<string, ProductPriceItem>> ValidatePricesAsync(
        IEnumerable<string> productIds, 
        CancellationToken cancellationToken = default);
}

public class PriceValidationService : IPriceValidationService
{
    private readonly CatalogPriceChecker.CatalogPriceCheckerClient _client;
    private readonly ILogger<PriceValidationService> _logger;

    public PriceValidationService(
        CatalogPriceChecker.CatalogPriceCheckerClient client,
        ILogger<PriceValidationService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<Dictionary<string, ProductPriceItem>> ValidatePricesAsync(
        IEnumerable<string> productIds, 
        CancellationToken cancellationToken = default)
    {
        var distinctIds = productIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
        if (distinctIds.Count == 0)
        {
            return new Dictionary<string, ProductPriceItem>();
        }

        var request = new GetItemPricesRequest();
        request.ProductIds.AddRange(distinctIds);

        _logger.LogInformation("Invoking gRPC CatalogPriceChecker for {Count} products.", distinctIds.Count);

        var response = await _client.GetItemPricesAsync(request, cancellationToken: cancellationToken);

        var dictionary = new Dictionary<string, ProductPriceItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in response.Items)
        {
            dictionary[item.ProductId] = item;
        }

        _logger.LogInformation("Received {Count} product price items from Catalog gRPC service.", dictionary.Count);
        return dictionary;
    }
}
