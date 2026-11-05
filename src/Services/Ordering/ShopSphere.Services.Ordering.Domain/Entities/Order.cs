using ShopSphere.Services.Ordering.Domain.Enums;
using ShopSphere.Services.Ordering.Domain.Exceptions;
using ShopSphere.Services.Ordering.Domain.ValueObjects;

namespace ShopSphere.Services.Ordering.Domain.Entities;

public class Order
{
    private readonly List<OrderItem> _items = new();

    public Guid Id { get; private set; }
    public string CustomerId { get; private set; } = string.Empty;
    public string CustomerEmail { get; private set; } = string.Empty;
    public Address ShippingAddress { get; private set; } = null!;
    public IReadOnlyCollection<OrderItem> Items => _items.AsReadOnly();
    public decimal TotalAmount { get; private set; }
    public OrderStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    private Order()
    {
    }

    public static Order Create(
        string customerId,
        string customerEmail,
        Address shippingAddress,
        IEnumerable<(string ProductId, string ProductName, decimal UnitPrice, int Quantity)> items)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(customerEmail);
        ArgumentNullException.ThrowIfNull(shippingAddress);
        ArgumentNullException.ThrowIfNull(items);

        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            CustomerEmail = customerEmail,
            ShippingAddress = shippingAddress,
            Status = OrderStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        foreach (var item in items)
        {
            order.AddItem(item.ProductId, item.ProductName, item.UnitPrice, item.Quantity);
        }

        if (order._items.Count == 0)
        {
            throw new OrderingDomainException("An order must contain at least one item.");
        }

        order.CalculateTotal();
        return order;
    }

    public void AddItem(string productId, string productName, decimal unitPrice, int quantity)
    {
        var item = new OrderItem(Id, productId, productName, unitPrice, quantity);
        _items.Add(item);
        CalculateTotal();
    }

    public void CalculateTotal()
    {
        TotalAmount = _items.Sum(i => i.TotalPrice);
    }

    public void Cancel()
    {
        if (Status == OrderStatus.Paid || Status == OrderStatus.Completed)
        {
            throw new OrderingDomainException($"Cannot cancel order in status '{Status}'. Only Pending or StockReserved orders can be cancelled.");
        }

        Status = OrderStatus.Cancelled;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void MarkStockReserved()
    {
        Status = OrderStatus.StockReserved;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void MarkPaid()
    {
        Status = OrderStatus.Paid;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void MarkCompleted()
    {
        Status = OrderStatus.Completed;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}

