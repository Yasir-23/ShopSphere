using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShopSphere.EventBus.Events;
using ShopSphere.Services.Ordering.Domain.Entities;
using ShopSphere.Services.Ordering.Domain.Enums;
using ShopSphere.Services.Ordering.Domain.ValueObjects;
using ShopSphere.Services.Ordering.Infrastructure.Persistence;
using ShopSphere.Services.Ordering.Infrastructure.Sagas;
using Xunit;

namespace ShopSphere.E2E.Tests;

public class CheckoutSagaE2ETests
{
    private static (ServiceProvider Provider, ITestHarness Harness, ISagaStateMachineTestHarness<OrderStateMachine, OrderStateData> SagaHarness, string DbName)
        CreateTestHarnessEnvironment()
    {
        var dbName = "Ordering_Saga_TestDb_" + Guid.NewGuid();
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.AddDebug());

        // Isolated in-memory db for order persistence
        services.AddDbContext<OrderingDbContext>(options =>
        {
            options.UseInMemoryDatabase(dbName);
        });

        services.AddMassTransitTestHarness(x =>
        {
            x.AddSagaStateMachine<OrderStateMachine, OrderStateData>()
                .InMemoryRepository();
        });

        var provider = services.BuildServiceProvider();
        var harness = provider.GetRequiredService<ITestHarness>();
        var sagaHarness = harness.GetSagaStateMachineHarness<OrderStateMachine, OrderStateData>();

        return (provider, harness, sagaHarness, dbName);
    }

    [Fact]
    public async Task HappyPath_CheckoutSaga_TransitionsToCompleted_AndSettlesOrder()
    {
        // Arrange
        var (provider, harness, sagaHarness, dbName) = CreateTestHarnessEnvironment();
        await using var serviceScope = provider.CreateAsyncScope();
        var db = serviceScope.ServiceProvider.GetRequiredService<OrderingDbContext>();

        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid().ToString();
        var items = new List<OrderItemDto>
        {
            new("PROD-001", "Mechanical Keyboard", 120.00m, 1, 120.00m),
            new("PROD-002", "Gaming Mouse", 60.00m, 2, 120.00m)
        };
        var totalAmount = 240.00m;

        // Seed initial pending order in DB
        var address = new Address("123 Main St", "Metropolis", "NY", "10001", "USA");
        var domainItems = items.Select(i => (i.ProductId, i.ProductName, i.UnitPrice, i.Quantity));
        var order = Order.Create(customerId, "buyer@example.com", address, domainItems);

        // Set ID to match saga OrderId via reflection since ID is private set
        typeof(Order).GetProperty("Id")!.SetValue(order, orderId);
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        await harness.Start();
        try
        {
            // Act 1: Order Created -> Transition to AwaitingInventory & Publish ReserveInventoryCommand
            var orderCreatedEvent = new OrderCreatedIntegrationEvent(
                orderId,
                customerId,
                totalAmount,
                items,
                DateTime.UtcNow);

            await harness.Bus.Publish(orderCreatedEvent);

            (await sagaHarness.Consumed.Any<OrderCreatedIntegrationEvent>()).Should().BeTrue();
            (await sagaHarness.Created.Any(s => s.CorrelationId == orderId)).Should().BeTrue();

            var sagaInstance = sagaHarness.Created.ContainsInState(orderId, sagaHarness.StateMachine, sagaHarness.StateMachine.AwaitingInventory);
            sagaInstance.Should().NotBeNull();
            (await harness.Published.Any<ReserveInventoryCommand>(c => c.Context.Message.OrderId == orderId)).Should().BeTrue();

            // Act 2: Inventory Reserved -> Transition to AwaitingPayment & Publish ProcessPaymentCommand
            var inventoryReservedEvent = new InventoryReservedIntegrationEvent(orderId, DateTime.UtcNow);
            await harness.Bus.Publish(inventoryReservedEvent);

            (await sagaHarness.Consumed.Any<InventoryReservedIntegrationEvent>()).Should().BeTrue();

            sagaInstance = sagaHarness.Created.ContainsInState(orderId, sagaHarness.StateMachine, sagaHarness.StateMachine.AwaitingPayment);
            sagaInstance.Should().NotBeNull();
            (await harness.Published.Any<ProcessPaymentCommand>(c => c.Context.Message.OrderId == orderId)).Should().BeTrue();

            // Act 3: Payment Processed -> Transition to Completed & Commit Order
            var paymentProcessedEvent = new PaymentProcessedIntegrationEvent(
                Guid.NewGuid(),
                orderId,
                totalAmount,
                "TXN-SUCCESS-12345",
                DateTime.UtcNow);
            await harness.Bus.Publish(paymentProcessedEvent);

            (await sagaHarness.Consumed.Any<PaymentProcessedIntegrationEvent>()).Should().BeTrue();

            sagaInstance = sagaHarness.Created.ContainsInState(orderId, sagaHarness.StateMachine, sagaHarness.StateMachine.Completed);
            sagaInstance.Should().NotBeNull();

            // Assert: Order in DB is marked Completed
            await using var assertScope = provider.CreateAsyncScope();
            var assertDb = assertScope.ServiceProvider.GetRequiredService<OrderingDbContext>();
            var dbOrder = await assertDb.Orders.FindAsync(orderId);
            dbOrder.Should().NotBeNull();
            dbOrder!.Status.Should().Be(OrderStatus.Completed);
        }
        finally
        {
            await harness.Stop();
        }
    }

    [Fact]
    public async Task CompensatingRollback_CheckoutSaga_WhenPaymentFails_ReleasesInventoryAndCancelsOrder()
    {
        // Arrange
        var (provider, harness, sagaHarness, dbName) = CreateTestHarnessEnvironment();
        await using var serviceScope = provider.CreateAsyncScope();
        var db = serviceScope.ServiceProvider.GetRequiredService<OrderingDbContext>();

        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid().ToString();
        var items = new List<OrderItemDto>
        {
            new("PROD-100", "Ultra HD Monitor", 450.00m, 1, 450.00m)
        };
        var totalAmount = 450.00m;

        // Seed initial pending order in DB
        var address = new Address("456 Market St", "Gotham", "NJ", "07001", "USA");
        var domainItems = items.Select(i => (i.ProductId, i.ProductName, i.UnitPrice, i.Quantity));
        var order = Order.Create(customerId, "client@example.com", address, domainItems);
        typeof(Order).GetProperty("Id")!.SetValue(order, orderId);
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        await harness.Start();
        try
        {
            // Step 1: Order Created -> AwaitingInventory
            await harness.Bus.Publish(new OrderCreatedIntegrationEvent(
                orderId, customerId, totalAmount, items, DateTime.UtcNow));

            (await sagaHarness.Consumed.Any<OrderCreatedIntegrationEvent>()).Should().BeTrue();
            sagaHarness.Created.ContainsInState(orderId, sagaHarness.StateMachine, sagaHarness.StateMachine.AwaitingInventory)
                .Should().NotBeNull();

            // Step 2: Inventory Reserved -> AwaitingPayment
            await harness.Bus.Publish(new InventoryReservedIntegrationEvent(orderId, DateTime.UtcNow));
            (await sagaHarness.Consumed.Any<InventoryReservedIntegrationEvent>()).Should().BeTrue();
            sagaHarness.Created.ContainsInState(orderId, sagaHarness.StateMachine, sagaHarness.StateMachine.AwaitingPayment)
                .Should().NotBeNull();

            // Step 3: Payment Fails -> Publish ReleaseInventoryCommand & Transition to Cancelled
            var paymentFailedEvent = new PaymentFailedIntegrationEvent(
                orderId,
                "Credit limit exceeded",
                DateTime.UtcNow);
            await harness.Bus.Publish(paymentFailedEvent);

            (await sagaHarness.Consumed.Any<PaymentFailedIntegrationEvent>()).Should().BeTrue();

            // Verify compensation command was published to release reserved stock
            (await harness.Published.Any<ReleaseInventoryCommand>(c =>
                c.Context.Message.OrderId == orderId &&
                c.Context.Message.Reason == "Credit limit exceeded")).Should().BeTrue();

            // Verify Saga transitioned to Cancelled
            var cancelledSaga = sagaHarness.Created.ContainsInState(orderId, sagaHarness.StateMachine, sagaHarness.StateMachine.Cancelled);
            cancelledSaga.Should().NotBeNull();

            // Verify Order status in DB is Cancelled
            await using var assertScope = provider.CreateAsyncScope();
            var assertDb = assertScope.ServiceProvider.GetRequiredService<OrderingDbContext>();
            var dbOrder = await assertDb.Orders.FindAsync(orderId);
            dbOrder.Should().NotBeNull();
            dbOrder!.Status.Should().Be(OrderStatus.Cancelled);
        }
        finally
        {
            await harness.Stop();
        }
    }

    [Fact]
    public async Task CompensatingRollback_CheckoutSaga_WhenInventoryReservationFails_CancelsOrder()
    {
        // Arrange
        var (provider, harness, sagaHarness, dbName) = CreateTestHarnessEnvironment();
        await using var serviceScope = provider.CreateAsyncScope();
        var db = serviceScope.ServiceProvider.GetRequiredService<OrderingDbContext>();

        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid().ToString();
        var items = new List<OrderItemDto>
        {
            new("PROD-999", "Rare Collectible", 999.00m, 1, 999.00m)
        };

        var address = new Address("789 Pine St", "Star City", "CA", "90210", "USA");
        var domainItems = items.Select(i => (i.ProductId, i.ProductName, i.UnitPrice, i.Quantity));
        var order = Order.Create(customerId, "collector@example.com", address, domainItems);
        typeof(Order).GetProperty("Id")!.SetValue(order, orderId);
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        await harness.Start();
        try
        {
            // Step 1: Order Created -> AwaitingInventory
            await harness.Bus.Publish(new OrderCreatedIntegrationEvent(
                orderId, customerId, 999.00m, items, DateTime.UtcNow));

            (await sagaHarness.Consumed.Any<OrderCreatedIntegrationEvent>()).Should().BeTrue();

            // Step 2: Inventory Reservation Failed -> Cancelled
            var inventoryFailedEvent = new InventoryReservationFailedIntegrationEvent(
                orderId,
                "Insufficient stock in warehouse",
                DateTime.UtcNow);
            await harness.Bus.Publish(inventoryFailedEvent);

            (await sagaHarness.Consumed.Any<InventoryReservationFailedIntegrationEvent>()).Should().BeTrue();

            // Verify Saga transitioned to Cancelled
            var cancelledSaga = sagaHarness.Created.ContainsInState(orderId, sagaHarness.StateMachine, sagaHarness.StateMachine.Cancelled);
            cancelledSaga.Should().NotBeNull();

            // Verify Order in DB is Cancelled
            await using var assertScope = provider.CreateAsyncScope();
            var assertDb = assertScope.ServiceProvider.GetRequiredService<OrderingDbContext>();
            var dbOrder = await assertDb.Orders.FindAsync(orderId);
            dbOrder.Should().NotBeNull();
            dbOrder!.Status.Should().Be(OrderStatus.Cancelled);
        }
        finally
        {
            await harness.Stop();
        }
    }
}
