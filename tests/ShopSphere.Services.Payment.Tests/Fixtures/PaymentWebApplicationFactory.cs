using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using ShopSphere.Services.Payment.Data;
using ShopSphere.Tests.Common;

namespace ShopSphere.Services.Payment.Tests.Fixtures;

public class PaymentWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = "PaymentTestDb_" + Guid.NewGuid();
    public Mock<IPublishEndpoint> MockPublishEndpoint { get; } = new();

    public PaymentWebApplicationFactory()
    {
        MockPublishEndpoint
            .Setup(p => p.Publish(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureTestServices(services =>
        {
            services.AddTestResponseBodyFeatureWorkaround();

            // Replace DbContext with EF Core InMemory provider
            var dbContextDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(DbContextOptions<PaymentDbContext>));
            if (dbContextDescriptor != null)
            {
                services.Remove(dbContextDescriptor);
            }

            var dbDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(PaymentDbContext));
            if (dbDescriptor != null)
            {
                services.Remove(dbDescriptor);
            }

            services.AddDbContext<PaymentDbContext>(options =>
            {
                options.UseInMemoryDatabase(_databaseName);
            });

            // Replace MassTransit IPublishEndpoint with Mock
            var publishEndpointDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IPublishEndpoint));
            if (publishEndpointDescriptor != null)
            {
                services.Remove(publishEndpointDescriptor);
            }
            services.AddSingleton<IPublishEndpoint>(MockPublishEndpoint.Object);
        });
    }

    public async Task ResetDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
    }
}

