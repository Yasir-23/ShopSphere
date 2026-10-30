using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ShopSphere.Services.Inventory.Tests.Fixtures;

public class LivePostgreSqlInventoryWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Uses the real PostgreSQL database configured in appsettings.json
        builder.UseEnvironment("Development");
    }
}

