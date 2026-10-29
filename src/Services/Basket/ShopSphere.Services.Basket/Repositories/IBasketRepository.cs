using ShopSphere.Services.Basket.Models;

namespace ShopSphere.Services.Basket.Repositories;

public interface IBasketRepository
{
    Task<ShoppingCart?> GetBasketAsync(string customerId);
    Task<ShoppingCart> UpdateBasketAsync(ShoppingCart basket);
    Task<bool> DeleteBasketAsync(string customerId);
}
