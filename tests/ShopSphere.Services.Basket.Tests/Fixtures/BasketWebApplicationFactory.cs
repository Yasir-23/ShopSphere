using Grpc.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using ShopSphere.Services.Basket.Repositories;
using ShopSphere.Services.Basket.Tests.Fakes;
using ShopSphere.Services.Catalog.Grpc;
using ShopSphere.Tests.Common;
using StackExchange.Redis;

namespace ShopSphere.Services.Basket.Tests.Fixtures;

public class BasketWebApplicationFactory : WebApplicationFactory<Program>
{
    public InMemoryBasketRepository Repository { get; } = new();
    public Mock<CatalogPriceChecker.CatalogPriceCheckerClient> MockCatalogClient { get; } = new();

    private readonly Dictionary<string, ProductPriceItem> _configuredPrices = new(StringComparer.OrdinalIgnoreCase);

    public BasketWebApplicationFactory()
    {
        MockCatalogClient
            .Setup(c => c.GetItemPricesAsync(
                It.IsAny<GetItemPricesRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns((GetItemPricesRequest request, Metadata? _, DateTime? _, CancellationToken _) =>
            {
                var response = new GetItemPricesResponse();
                foreach (var id in request.ProductIds)
                {
                    if (_configuredPrices.TryGetValue(id, out var item))
                    {
                        response.Items.Add(item);
                    }
                }

                return new AsyncUnaryCall<GetItemPricesResponse>(
                    Task.FromResult(response),
                    Task.FromResult(new Metadata()),
                    () => Status.DefaultSuccess,
                    () => new Metadata(),
                    () => { });
            });
    }

    public void SetupCanonicalPrices(params ProductPriceItem[] items)
    {
        _configuredPrices.Clear();
        foreach (var item in items)
        {
            _configuredPrices[item.ProductId] = item;
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureTestServices(services =>
        {
            services.AddTestResponseBodyFeatureWorkaround();

            // Remove Redis connection multiplexer
            var redisDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IConnectionMultiplexer));
            if (redisDescriptor != null)
            {
                services.Remove(redisDescriptor);
            }

            // Replace Redis repository with InMemory repository
            var repoDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IBasketRepository));
            if (repoDescriptor != null)
            {
                services.Remove(repoDescriptor);
            }
            services.AddSingleton<IBasketRepository>(Repository);

            // Replace gRPC client with Mock client
            var clientDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(CatalogPriceChecker.CatalogPriceCheckerClient));
            if (clientDescriptor != null)
            {
                services.Remove(clientDescriptor);
            }
            services.AddSingleton(MockCatalogClient.Object);
        });
    }
}
