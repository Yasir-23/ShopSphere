namespace ShopSphere.Services.Inventory.Entities;

public class InventoryReservation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public Guid InventoryItemId { get; set; }
    public string ProductId { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public DateTime ReservedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsReleased { get; set; }
    public bool IsCommitted { get; set; }

    public InventoryItem? InventoryItem { get; set; }
}

