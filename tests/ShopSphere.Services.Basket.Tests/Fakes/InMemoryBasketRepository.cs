using System.Collections.Concurrent;
using ShopSphere.Services.Basket.Models;
using ShopSphere.Services.Basket.Repositories;

namespace ShopSphere.Services.Basket.Tests.Fakes;

public class InMemoryBasketRepository : IBasketRepository
{
    private readonly ConcurrentDictionary<string, ShoppingCart> _store = new(StringComparer.OrdinalIgnoreCase);

    public Task<ShoppingCart?> GetBasketAsync(string customerId)
    {
        _store.TryGetValue(customerId, out var basket);
        return Task.FromResult(basket);
    }

    public Task<ShoppingCart> UpdateBasketAsync(ShoppingCart basket)
    {
        _store[basket.CustomerId] = basket;
        return Task.FromResult(basket);
    }

    public Task<bool> DeleteBasketAsync(string customerId)
    {
        var removed = _store.TryRemove(customerId, out _);
        return Task.FromResult(removed);
    }

    public void Reset()
    {
        _store.Clear();
    }
}
