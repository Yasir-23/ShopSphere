using Microsoft.Extensions.Options;
using MongoDB.Driver;
using ShopSphere.Services.Catalog.Api.Configuration;
using ShopSphere.Services.Catalog.Api.Models;

namespace ShopSphere.Services.Catalog.Api.Data;

public interface ICatalogDbContext
{
    IMongoCollection<Product> Products { get; }
    IMongoCollection<Category> Categories { get; }
    Task InitializeIndicesAsync(CancellationToken cancellationToken = default);
}

public class CatalogDbContext : ICatalogDbContext
{
    private readonly IMongoDatabase _database;
    private readonly MongoDbOptions _options;
    private readonly ILogger<CatalogDbContext> _logger;

    public CatalogDbContext(
        IOptions<MongoDbOptions> options,
        ILogger<CatalogDbContext> logger)
    {
        _options = options.Value;
        _logger = logger;

        var mongoUrl = new MongoUrl(_options.ConnectionString);
        var client = new MongoClient(mongoUrl);
        var databaseName = !string.IsNullOrWhiteSpace(mongoUrl.DatabaseName)
            ? mongoUrl.DatabaseName
            : _options.DatabaseName;
        _database = client.GetDatabase(databaseName);
    }

    public IMongoCollection<Product> Products =>
        _database.GetCollection<Product>(_options.ProductsCollectionName);

    public IMongoCollection<Category> Categories =>
        _database.GetCollection<Category>(_options.CategoriesCollectionName);

    public async Task InitializeIndicesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Initializing MongoDB indices for Catalog Service...");

            // 1. Text Index on { Name: "text", Description: "text" }
            var textIndexKeys = Builders<Product>.IndexKeys
                .Text(p => p.Name)
                .Text(p => p.Description);
            var textIndexModel = new CreateIndexModel<Product>(
                textIndexKeys,
                new CreateIndexOptions
                {
                    Name = "idx_products_text_name_desc",
                    Background = true
                });

            // 2. Compound Index on { Category: 1, Price: 1 }
            var compoundIndexKeys = Builders<Product>.IndexKeys
                .Ascending(p => p.Category)
                .Ascending(p => p.Price);
            var compoundIndexModel = new CreateIndexModel<Product>(
                compoundIndexKeys,
                new CreateIndexOptions
                {
                    Name = "idx_products_category_price",
                    Background = true
                });

            // 3. Unique Index on Sku
            var skuIndexKeys = Builders<Product>.IndexKeys.Ascending(p => p.Sku);
            var skuIndexModel = new CreateIndexModel<Product>(
                skuIndexKeys,
                new CreateIndexOptions
                {
                    Name = "idx_products_sku_unique",
                    Unique = true,
                    Background = true
                });

            await Products.Indexes.CreateManyAsync(
                new[] { textIndexModel, compoundIndexModel, skuIndexModel },
                cancellationToken);

            _logger.LogInformation("MongoDB indices initialized successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize MongoDB indices for Products collection.");
            throw;
        }
    }
}
