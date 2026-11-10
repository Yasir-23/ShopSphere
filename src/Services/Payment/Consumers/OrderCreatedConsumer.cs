using MassTransit;
using Microsoft.EntityFrameworkCore;
using ShopSphere.Services.Ordering.Application.Events;
using ShopSphere.Services.Payment.Data;
using ShopSphere.Services.Payment.Entities;
using ShopSphere.Services.Payment.Services;

namespace ShopSphere.Services.Payment.Consumers;

public class OrderCreatedConsumer : IConsumer<OrderCreatedIntegrationEvent>
{
    private readonly PaymentDbContext _dbContext;
    private readonly ILogger<OrderCreatedConsumer> _logger;

    public OrderCreatedConsumer(
        PaymentDbContext dbContext,
        ILogger<OrderCreatedConsumer> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<OrderCreatedIntegrationEvent> context)
    {
        var message = context.Message;
        _logger.LogInformation("OrderCreatedConsumer received event for OrderId: {OrderId}, Total: {Amount}",
            message.OrderId, message.TotalAmount);

        var existing = await _dbContext.Transactions
            .FirstOrDefaultAsync(t => t.OrderId == message.OrderId, context.CancellationToken);

        if (existing != null)
        {
            _logger.LogInformation("PaymentTransaction already exists for OrderId: {OrderId}", message.OrderId);
            return;
        }

        var transaction = new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            OrderId = message.OrderId,
            Amount = message.TotalAmount,
            Currency = "USD",
            Status = PaymentStatus.Pending,
            PaymentMethod = "Stripe",
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.Transactions.Add(transaction);
        try
        {
            await _dbContext.SaveChangesAsync(context.CancellationToken);
        }
        catch (DbUpdateException ex) when (PaymentService.IsUniqueViolation(ex))
        {
            // A payment request created the row first: nothing left to register.
            _logger.LogInformation("PaymentTransaction for OrderId {OrderId} was created concurrently; skipping.", message.OrderId);
            return;
        }

        _logger.LogInformation("Created initial Pending PaymentTransaction {TransactionId} for OrderId {OrderId}.",
            transaction.Id, message.OrderId);
    }
}

