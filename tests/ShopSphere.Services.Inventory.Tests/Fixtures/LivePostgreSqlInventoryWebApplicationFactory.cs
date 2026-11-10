using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using ShopSphere.Tests.Common;

namespace ShopSphere.Services.Inventory.Tests.Fixtures;

public class LivePostgreSqlInventoryWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Uses the real PostgreSQL database configured in appsettings.json
        builder.UseEnvironment("Development");
        builder.ConfigureTestServices(services =>
        {

            // The Inventory service now hosts MassTransit consumers; keep tests broker-free.
            foreach (var hosted in services
                .Where(d => d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService) &&
                            d.ImplementationType?.FullName?.Contains("MassTransit") == true)
                .ToList())
            {
                services.Remove(hosted);
            }
            services.AddTestResponseBodyFeatureWorkaround();
        });
    }
}

