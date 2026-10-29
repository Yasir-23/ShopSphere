using MongoDB.Driver;
using ShopSphere.Services.Catalog.Api.Models;

namespace ShopSphere.Services.Catalog.Api.Data;

public static class CatalogDbSeeder
{
    public static async Task SeedAsync(ICatalogDbContext context, ILogger logger, CancellationToken cancellationToken = default)
    {
        try
        {
            await context.InitializeIndicesAsync(cancellationToken);

            // 1. Seed Categories if empty
            var categoryCount = await context.Categories.CountDocumentsAsync(
                FilterDefinition<Category>.Empty, 
                cancellationToken: cancellationToken);

            Dictionary<string, string> categoryNameToId = new();

            if (categoryCount == 0)
            {
                logger.LogInformation("Seeding initial categories for Catalog Service...");

                var categories = new List<Category>
                {
                    new()
                    {
                        Id = "660c1d2e5a4b3c2d1e0f0001",
                        Name = "Electronics",
                        Description = "Cutting-edge consumer electronics, gadgets, and tech accessories."
                    },
                    new()
                    {
                        Id = "660c1d2e5a4b3c2d1e0f0002",
                        Name = "Peripherals",
                        Description = "High-performance computer keyboards, mice, audio gear, and productivity peripherals."
                    },
                    new()
                    {
                        Id = "660c1d2e5a4b3c2d1e0f0003",
                        Name = "Smart Home",
                        Description = "Connected smart home devices, lighting, sensors, and home automation gear."
                    }
                };

                await context.Categories.InsertManyAsync(categories, cancellationToken: cancellationToken);
                foreach (var cat in categories)
                {
                    categoryNameToId[cat.Name] = cat.Id;
                }
                logger.LogInformation("Seeded {Count} initial categories.", categories.Count);
            }
            else
            {
                var existingCategories = await context.Categories.Find(FilterDefinition<Category>.Empty)
                    .ToListAsync(cancellationToken);
                foreach (var cat in existingCategories)
                {
                    categoryNameToId[cat.Name] = cat.Id;
                }
            }

            // 2. Seed Products if empty
            var productCount = await context.Products.CountDocumentsAsync(
                FilterDefinition<Product>.Empty, 
                cancellationToken: cancellationToken);

            if (productCount == 0)
            {
                logger.LogInformation("Seeding initial products for Catalog Service...");

                var products = new List<Product>
                {
                    new()
                    {
                        Id = "660c1d2e5a4b3c2d1e0f1001",
                        Name = "Wireless Mechanical Gaming Keyboard",
                        Slug = "wireless-mechanical-gaming-keyboard",
                        Description = "RGB mechanical gaming keyboard with hot-swappable red switches, tri-mode connectivity, and PBT keycaps.",
                        Sku = "KB-WL-RED-001",
                        Price = 129.99m,
                        Currency = "USD",
                        Category = "Peripherals",
                        CategoryId = categoryNameToId.GetValueOrDefault("Peripherals", "660c1d2e5a4b3c2d1e0f0002"),
                        Attributes = new Dictionary<string, string>
                        {
                            { "switchType", "Red Linear" },
                            { "connectivity", "2.4GHz / Bluetooth 5.2 / USB-C" },
                            { "backlight", "Per-Key RGB" },
                            { "batteryCapacity", "4000mAh" }
                        },
                        Images = new List<string> { "https://cdn.shopsphere.local/products/keyboard-red.jpg" },
                        IsAvailable = true,
                        CreatedAtUtc = DateTime.UtcNow.AddDays(-10),
                        UpdatedAtUtc = DateTime.UtcNow.AddDays(-10)
                    },
                    new()
                    {
                        Id = "660c1d2e5a4b3c2d1e0f1002",
                        Name = "Active Noise Cancelling Wireless Headphones",
                        Slug = "active-noise-cancelling-wireless-headphones",
                        Description = "Premium over-ear wireless headphones with adaptive hybrid ANC, 40-hour playtime, and Hi-Res LDAC audio.",
                        Sku = "HP-ANC-BLK-002",
                        Price = 249.99m,
                        Currency = "USD",
                        Category = "Electronics",
                        CategoryId = categoryNameToId.GetValueOrDefault("Electronics", "660c1d2e5a4b3c2d1e0f0001"),
                        Attributes = new Dictionary<string, string>
                        {
                            { "ancMode", "Adaptive Hybrid" },
                            { "batteryLife", "40 Hours" },
                            { "codec", "LDAC / AAC / SBC" },
                            { "driverSize", "40mm Beryllium" }
                        },
                        Images = new List<string> { "https://cdn.shopsphere.local/products/headphones-anc.jpg" },
                        IsAvailable = true,
                        CreatedAtUtc = DateTime.UtcNow.AddDays(-8),
                        UpdatedAtUtc = DateTime.UtcNow.AddDays(-8)
                    },
                    new()
                    {
                        Id = "660c1d2e5a4b3c2d1e0f1003",
                        Name = "Ergonomic Ultra-Light Wireless Mouse",
                        Slug = "ergonomic-ultra-light-wireless-mouse",
                        Description = "Ultra-lightweight 58g wireless gaming mouse with 26,000 DPI optical sensor and 8,000Hz polling rate.",
                        Sku = "MS-ERGO-WHT-003",
                        Price = 89.99m,
                        Currency = "USD",
                        Category = "Peripherals",
                        CategoryId = categoryNameToId.GetValueOrDefault("Peripherals", "660c1d2e5a4b3c2d1e0f0002"),
                        Attributes = new Dictionary<string, string>
                        {
                            { "sensor", "Focus Pro 26K DPI" },
                            { "weight", "58 grams" },
                            { "pollingRate", "8000Hz" },
                            { "batteryLife", "90 Hours" }
                        },
                        Images = new List<string> { "https://cdn.shopsphere.local/products/mouse-ergo.jpg" },
                        IsAvailable = true,
                        CreatedAtUtc = DateTime.UtcNow.AddDays(-5),
                        UpdatedAtUtc = DateTime.UtcNow.AddDays(-5)
                    },
                    new()
                    {
                        Id = "660c1d2e5a4b3c2d1e0f1004",
                        Name = "Smart Ambient RGBIC LED Strip 2M",
                        Slug = "smart-ambient-rgbic-led-strip-2m",
                        Description = "2-meter Matter-certified addressable RGBIC light strip with Wi-Fi control, dynamic music sync, and 16M colors.",
                        Sku = "SH-LED-RGBIC-004",
                        Price = 49.99m,
                        Currency = "USD",
                        Category = "Smart Home",
                        CategoryId = categoryNameToId.GetValueOrDefault("Smart Home", "660c1d2e5a4b3c2d1e0f0003"),
                        Attributes = new Dictionary<string, string>
                        {
                            { "length", "2 Meters" },
                            { "protocol", "Matter over Wi-Fi" },
                            { "colorGamut", "RGBIC 16 Million Colors" },
                            { "voltage", "12V DC" }
                        },
                        Images = new List<string> { "https://cdn.shopsphere.local/products/led-strip.jpg" },
                        IsAvailable = true,
                        CreatedAtUtc = DateTime.UtcNow.AddDays(-3),
                        UpdatedAtUtc = DateTime.UtcNow.AddDays(-3)
                    },
                    new()
                    {
                        Id = "660c1d2e5a4b3c2d1e0f1005",
                        Name = "Thunderbolt 4 Dual 4K Display Dock",
                        Slug = "thunderbolt-4-dual-4k-display-dock",
                        Description = "12-in-1 Thunderbolt 4 docking station with 90W host power delivery, dual 4K @ 60Hz video, and 2.5GbE Ethernet.",
                        Sku = "DK-TB4-PRO-005",
                        Price = 199.99m,
                        Currency = "USD",
                        Category = "Electronics",
                        CategoryId = categoryNameToId.GetValueOrDefault("Electronics", "660c1d2e5a4b3c2d1e0f0001"),
                        Attributes = new Dictionary<string, string>
                        {
                            { "portsCount", "12" },
                            { "powerDelivery", "90W Host Charging" },
                            { "videoOutput", "Dual 4K @ 60Hz or Single 8K @ 30Hz" },
                            { "ethernet", "2.5 Gigabit RJ45" }
                        },
                        Images = new List<string> { "https://cdn.shopsphere.local/products/dock-tb4.jpg" },
                        IsAvailable = true,
                        CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
                        UpdatedAtUtc = DateTime.UtcNow.AddDays(-1)
                    }
                };

                await context.Products.InsertManyAsync(products, cancellationToken: cancellationToken);
                logger.LogInformation("Seeded {Count} initial products with SKUs and attributes.", products.Count);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while seeding Catalog MongoDB datastore.");
            // Allow app to boot even if Mongo is down, but log error clearly
        }
    }
}
