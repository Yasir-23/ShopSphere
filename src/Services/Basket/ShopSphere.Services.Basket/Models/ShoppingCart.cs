namespace ShopSphere.Services.Basket.Models;

public class ShoppingCart
{
    public string CustomerId { get; set; } = string.Empty;
    public List<ShoppingCartItem> Items { get; set; } = new();
    public decimal TotalPrice => Items.Sum(item => item.TotalPrice);
    public decimal TotalAmount => TotalPrice;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public ShoppingCart() { }

    public ShoppingCart(string customerId)
    {
        CustomerId = customerId;
    }
}
