using System.Text.Json;
using Microsoft.Extensions.Options;
using ShopSphere.Services.Basket.Configuration;
using ShopSphere.Services.Basket.Models;
using StackExchange.Redis;

namespace ShopSphere.Services.Basket.Repositories;

public class RedisBasketRepository : IBasketRepository
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _database;
    private readonly RedisOptions _options;
    private readonly ILogger<RedisBasketRepository> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public RedisBasketRepository(
        IConnectionMultiplexer redis,
        IOptions<RedisOptions> options,
        ILogger<RedisBasketRepository> logger)
    {
        _redis = redis;
        _options = options.Value;
        _database = _redis.GetDatabase();
        _logger = logger;
    }

    private static string GetBasketKey(string customerId) => $"basket:{customerId}";

    public async Task<ShoppingCart?> GetBasketAsync(string customerId)
    {
        var key = GetBasketKey(customerId);
        var data = await _database.StringGetAsync(key);

        if (data.IsNullOrEmpty)
        {
            return null;
        }

        return JsonSerializer.Deserialize<ShoppingCart>(data!, JsonOptions);
    }

    public async Task<ShoppingCart> UpdateBasketAsync(ShoppingCart basket)
    {
        var key = GetBasketKey(basket.CustomerId);
        basket.UpdatedAtUtc = DateTime.UtcNow;

        var json = JsonSerializer.Serialize(basket, JsonOptions);
        var slidingTtl = TimeSpan.FromDays(_options.SlidingExpirationDays);

        var created = await _database.StringSetAsync(key, json, slidingTtl);
        if (!created)
        {
            _logger.LogError("Failed to persist shopping basket in Redis for customer: {CustomerId}", basket.CustomerId);
            throw new InvalidOperationException($"Problem occurred persisting basket for customer '{basket.CustomerId}' in Redis.");
        }

        _logger.LogInformation("Successfully updated basket in Redis for customer {CustomerId} with TTL of {Days} days.", basket.CustomerId, _options.SlidingExpirationDays);
        return basket;
    }

    public async Task<bool> DeleteBasketAsync(string customerId)
    {
        var key = GetBasketKey(customerId);
        var deleted = await _database.KeyDeleteAsync(key);
        _logger.LogInformation("Deleted basket for customer {CustomerId}: {Deleted}", customerId, deleted);
        return deleted;
    }
}
