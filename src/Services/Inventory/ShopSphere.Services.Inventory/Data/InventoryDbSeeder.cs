using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShopSphere.Services.Inventory.Entities;

namespace ShopSphere.Services.Inventory.Data;

public static class InventoryDbSeeder
{
    public static async Task SeedAsync(InventoryDbContext context, ILogger logger, CancellationToken cancellationToken = default)
    {
        try
        {
            if (await context.InventoryItems.AnyAsync(cancellationToken))
            {
                logger.LogInformation("Inventory stock already seeded. Skipping initial seeding.");
                return;
            }

            logger.LogInformation("Seeding initial stock levels for catalog products...");

            var seedItems = new List<InventoryItem>
            {
                new()
                {
                    Id = Guid.Parse("11111111-1111-1111-1111-111111111001"),
                    ProductId = "660c1d2e5a4b3c2d1e0f1001",
                    Sku = "KB-WL-RED-001",
                    QuantityOnHand = 100,
                    QuantityReserved = 0,
                    RestockThreshold = 10,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                },
                new()
                {
                    Id = Guid.Parse("11111111-1111-1111-1111-111111111002"),
                    ProductId = "660c1d2e5a4b3c2d1e0f1002",
                    Sku = "HP-ANC-BLK-002",
                    QuantityOnHand = 75,
                    QuantityReserved = 0,
                    RestockThreshold = 10,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                },
                new()
                {
                    Id = Guid.Parse("11111111-1111-1111-1111-111111111003"),
                    ProductId = "660c1d2e5a4b3c2d1e0f1003",
                    Sku = "MS-ERGO-WHT-003",
                    QuantityOnHand = 80,
                    QuantityReserved = 0,
                    RestockThreshold = 15,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                },
                new()
                {
                    Id = Guid.Parse("11111111-1111-1111-1111-111111111004"),
                    ProductId = "660c1d2e5a4b3c2d1e0f1004",
                    Sku = "SH-LED-RGBIC-004",
                    QuantityOnHand = 60,
                    QuantityReserved = 0,
                    RestockThreshold = 10,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                },
                new()
                {
                    Id = Guid.Parse("11111111-1111-1111-1111-111111111005"),
                    ProductId = "660c1d2e5a4b3c2d1e0f1005",
                    Sku = "DK-TB4-PRO-005",
                    QuantityOnHand = 50,
                    QuantityReserved = 0,
                    RestockThreshold = 5,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                }
            };

            await context.InventoryItems.AddRangeAsync(seedItems, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Successfully seeded {Count} inventory items.", seedItems.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while seeding Inventory stock.");
            throw;
        }
    }
}

