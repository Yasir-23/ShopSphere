using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopSphere.Services.Inventory.Data;
using ShopSphere.Tests.Common;

namespace ShopSphere.Services.Inventory.Tests.Fixtures;

public class InventoryWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = "ShopSphere_Inventory_TestDb_" + Guid.NewGuid();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureTestServices(services =>
        {
            services.AddTestResponseBodyFeatureWorkaround();

            // Remove existing DbContextOptions
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<InventoryDbContext>));
            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            // Replace with unique InMemory database per factory instance
            services.AddDbContext<InventoryDbContext>(options =>
            {
                options.UseInMemoryDatabase(_databaseName);
            });
        });
    }
}

