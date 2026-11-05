using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using ShopSphere.Services.Ordering.Application.DTOs;
using ShopSphere.Services.Ordering.Application.Events;
using ShopSphere.Services.Ordering.Application.Repositories;
using ShopSphere.Services.Ordering.Domain.Entities;
using ShopSphere.Services.Ordering.Domain.ValueObjects;

namespace ShopSphere.Services.Ordering.Application.Commands.CreateOrder;

public class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, OrderResponse>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<CreateOrderCommandHandler> _logger;

    public CreateOrderCommandHandler(
        IOrderRepository orderRepository,
        IPublishEndpoint publishEndpoint,
        ILogger<CreateOrderCommandHandler> logger)
    {
        _orderRepository = orderRepository;
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task<OrderResponse> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
    {
        var address = new Address(
            request.ShippingAddress.Street,
            request.ShippingAddress.City,
            request.ShippingAddress.State,
            request.ShippingAddress.ZipCode,
            request.ShippingAddress.Country);

        var itemTuples = request.Items.Select(i => (
            i.ProductId,
            i.ProductName,
            i.UnitPrice,
            i.Quantity));

        var order = Order.Create(request.CustomerId, request.CustomerEmail, address, itemTuples);

        await _orderRepository.AddAsync(order, cancellationToken);
        await _orderRepository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Order {OrderId} created successfully with status {Status} and total amount {TotalAmount}.",
            order.Id, order.Status, order.TotalAmount);

        // Publish OrderCreatedIntegrationEvent
        var integrationEventItems = order.Items.Select(i => new OrderItemDto(
            i.ProductId,
            i.ProductName,
            i.UnitPrice,
            i.Quantity,
            i.TotalPrice)).ToList();

        var orderCreatedEvent = new OrderCreatedIntegrationEvent(
            order.Id,
            order.CustomerId,
            order.TotalAmount,
            integrationEventItems,
            order.CreatedAtUtc);

        try
        {
            await _publishEndpoint.Publish(orderCreatedEvent, cancellationToken);
            _logger.LogInformation("Published OrderCreatedIntegrationEvent for Order {OrderId}.", order.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish OrderCreatedIntegrationEvent for Order {OrderId}.", order.Id);
            // In case of broker unavailability without outbox, log and continue or outbox will buffer
        }

        return MapToOrderResponse(order);
    }

    private static OrderResponse MapToOrderResponse(Order order) => new(
        order.Id,
        order.CustomerId,
        order.CustomerEmail,
        new AddressDto(
            order.ShippingAddress.Street,
            order.ShippingAddress.City,
            order.ShippingAddress.State,
            order.ShippingAddress.ZipCode,
            order.ShippingAddress.Country),
        order.Items.Select(i => new OrderItemResponse(
            i.Id,
            i.ProductId,
            i.ProductName,
            i.UnitPrice,
            i.Quantity,
            i.TotalPrice)).ToList(),
        order.TotalAmount,
        order.Status.ToString(),
        order.CreatedAtUtc,
        order.UpdatedAtUtc);
}

