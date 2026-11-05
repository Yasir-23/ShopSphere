namespace ShopSphere.Services.Ordering.Domain.Entities;

public class OrderItem
{
    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public string ProductId { get; private set; } = string.Empty;
    public string ProductName { get; private set; } = string.Empty;
    public decimal UnitPrice { get; private set; }
    public int Quantity { get; private set; }
    public decimal TotalPrice => UnitPrice * Quantity;

    private OrderItem()
    {
    }

    public OrderItem(Guid orderId, string productId, string productName, decimal unitPrice, int quantity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);
        ArgumentException.ThrowIfNullOrWhiteSpace(productName);
        if (unitPrice <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unitPrice), "Unit price must be greater than zero.");
        }
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be greater than zero.");
        }

        Id = Guid.NewGuid();
        OrderId = orderId;
        ProductId = productId;
        ProductName = productName;
        UnitPrice = unitPrice;
        Quantity = quantity;
    }
}

