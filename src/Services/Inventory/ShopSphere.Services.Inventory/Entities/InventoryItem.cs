namespace ShopSphere.Services.Inventory.Entities;

public class InventoryItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ProductId { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public int QuantityOnHand { get; set; }
    public int QuantityReserved { get; set; }
    public int QuantityAvailable => QuantityOnHand - QuantityReserved;
    public int RestockThreshold { get; set; }
    public byte[] RowVersion { get; set; } = Guid.NewGuid().ToByteArray();
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public bool HasSufficientStock(int quantity)
    {
        return quantity > 0 && QuantityAvailable >= quantity;
    }

    public void Reserve(int quantity)
    {
        if (quantity <= 0)
        {
            throw new ArgumentException("Quantity to reserve must be greater than zero.", nameof(quantity));
        }

        if (QuantityAvailable < quantity)
        {
            throw new InvalidOperationException($"Insufficient stock available for Product '{ProductId}'. Available: {QuantityAvailable}, Requested: {quantity}.");
        }

        QuantityReserved += quantity;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void Release(int quantity)
    {
        if (quantity <= 0)
        {
            throw new ArgumentException("Quantity to release must be greater than zero.", nameof(quantity));
        }

        QuantityReserved = Math.Max(0, QuantityReserved - quantity);
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void Commit(int quantity)
    {
        if (quantity <= 0)
        {
            throw new ArgumentException("Quantity to commit must be greater than zero.", nameof(quantity));
        }

        QuantityOnHand = Math.Max(0, QuantityOnHand - quantity);
        QuantityReserved = Math.Max(0, QuantityReserved - quantity);
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void Restock(int quantity)
    {
        if (quantity <= 0)
        {
            throw new ArgumentException("Quantity to restock must be greater than zero.", nameof(quantity));
        }

        QuantityOnHand += quantity;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void Adjust(int newQuantityOnHand)
    {
        if (newQuantityOnHand < 0)
        {
            throw new ArgumentException("Quantity on hand cannot be negative.", nameof(newQuantityOnHand));
        }

        QuantityOnHand = newQuantityOnHand;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}

