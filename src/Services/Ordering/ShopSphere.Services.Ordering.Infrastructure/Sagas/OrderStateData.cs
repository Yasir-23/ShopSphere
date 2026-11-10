using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using MassTransit;
using ShopSphere.EventBus.Events;

namespace ShopSphere.Services.Ordering.Infrastructure.Sagas;

public class OrderStateData : SagaStateMachineInstance
{
    public Guid CorrelationId { get; set; }
    public string CurrentState { get; set; } = string.Empty;
    public Guid OrderId { get; set; }
    public string CustomerId { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public string? ItemsJson { get; set; }

    [NotMapped]
    public List<OrderItemDto> Items
    {
        get => string.IsNullOrWhiteSpace(ItemsJson)
            ? new List<OrderItemDto>()
            : JsonSerializer.Deserialize<List<OrderItemDto>>(ItemsJson) ?? new List<OrderItemDto>();
        set => ItemsJson = JsonSerializer.Serialize(value);
    }

    public string? FailureReason { get; set; }
    public string? PaymentMethod { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public byte[]? RowVersion { get; set; }
}
