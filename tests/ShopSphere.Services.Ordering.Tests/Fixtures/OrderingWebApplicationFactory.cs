using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using ShopSphere.Services.Ordering.Infrastructure.Persistence;
using ShopSphere.Tests.Common;

namespace ShopSphere.Services.Ordering.Tests.Fixtures;

public class OrderingWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = "ShopSphere_Ordering_TestDb_" + Guid.NewGuid();
    public Mock<IPublishEndpoint> MockPublishEndpoint { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureTestServices(services =>
        {
            services.AddTestResponseBodyFeatureWorkaround();

            // 1. Replace SQL Server DbContext with isolated InMemory DbContext
            var dbDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<OrderingDbContext>));
            if (dbDescriptor != null)
            {
                services.Remove(dbDescriptor);
            }

            services.AddDbContext<OrderingDbContext>(options =>
            {
                options.UseInMemoryDatabase(_databaseName);
            });

            // 2. Remove MassTransit background bus hosted services to prevent broker connection attempts
            var massTransitHostedServices = services
                .Where(d => d.ServiceType == typeof(IHostedService) && 
                            d.ImplementationType != null && 
                            d.ImplementationType.FullName != null && 
                            d.ImplementationType.FullName.Contains("MassTransit"))
                .ToList();

            foreach (var hs in massTransitHostedServices)
            {
                services.Remove(hs);
            }

            // 3. Mock IPublishEndpoint for integration events
            var publishDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IPublishEndpoint));
            if (publishDescriptor != null)
            {
                services.Remove(publishDescriptor);
            }
            services.AddSingleton(MockPublishEndpoint.Object);
        });
    }
}

