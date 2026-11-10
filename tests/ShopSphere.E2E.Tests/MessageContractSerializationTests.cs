using System.Text.Json;
using FluentAssertions;
using MassTransit.Serialization;
using ShopSphere.EventBus.Events;
using Xunit;

namespace ShopSphere.E2E.Tests;

/// <summary>
/// Every message that crosses RabbitMQ must survive MassTransit's System.Text.Json
/// serializer. The in-memory saga harness does not serialize, so this test guards
/// the wire contract (a type with several parameterized constructors cannot be
/// deserialized, and its consumer faults before running).
/// </summary>
public class MessageContractSerializationTests
{
    public static IEnumerable<object[]> ContractTypes() =>
        typeof(IntegrationEvent).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true } && typeof(IntegrationEvent).IsAssignableFrom(t))
            .Select(t => new object[] { t.Name });

    [Theory]
    [MemberData(nameof(ContractTypes))]
    public void Contract_DeserializesWithMassTransitSerializerOptions(string typeName)
    {
        var type = typeof(IntegrationEvent).Assembly.GetTypes().Single(t => t.Name == typeName);

        var act = () => JsonSerializer.Deserialize("{}", type, SystemTextJsonMessageSerializer.Options);

        act.Should().NotThrow($"{type.Name} must be deserializable by consumers");
    }

    [Fact]
    public void ReserveInventoryCommand_RoundTripsItems()
    {
        var orderId = Guid.NewGuid();
        var command = new ReserveInventoryCommand(orderId,
            new List<OrderItemDto> { new("660c1d2e5a4b3c2d1e0f1003", "Mouse", 89.99m, 2, 179.98m) },
            DateTimeOffset.UtcNow);

        var json = JsonSerializer.Serialize(command, SystemTextJsonMessageSerializer.Options);
        var back = JsonSerializer.Deserialize<ReserveInventoryCommand>(json, SystemTextJsonMessageSerializer.Options)!;

        back.OrderId.Should().Be(orderId);
        back.Items.Should().ContainSingle(i => i.ProductId == "660c1d2e5a4b3c2d1e0f1003" && i.Quantity == 2);
    }

    [Fact]
    public void ProcessPaymentCommand_RoundTripsPaymentMethod()
    {
        var command = new ProcessPaymentCommand(Guid.NewGuid(), Guid.NewGuid(), 10m, DateTimeOffset.UtcNow) { PaymentMethod = "card_visa_4242" };

        var json = JsonSerializer.Serialize(command, SystemTextJsonMessageSerializer.Options);
        JsonSerializer.Deserialize<ProcessPaymentCommand>(json, SystemTextJsonMessageSerializer.Options)!
            .PaymentMethod.Should().Be("card_visa_4242");
    }
}
