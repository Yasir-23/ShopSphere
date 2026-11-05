using MediatR;
using Microsoft.Extensions.Logging;
using ShopSphere.Services.Ordering.Application.DTOs;
using ShopSphere.Services.Ordering.Application.Repositories;
using ShopSphere.Services.Ordering.Domain.Entities;
using ShopSphere.Services.Ordering.Domain.Exceptions;

namespace ShopSphere.Services.Ordering.Application.Commands.CancelOrder;

public class CancelOrderCommandHandler : IRequestHandler<CancelOrderCommand, CancelOrderResult>
{
    private readonly IOrderRepository _orderRepository;
    private readonly ILogger<CancelOrderCommandHandler> _logger;

    public CancelOrderCommandHandler(
        IOrderRepository orderRepository,
        ILogger<CancelOrderCommandHandler> logger)
    {
        _orderRepository = orderRepository;
        _logger = logger;
    }

    public async Task<CancelOrderResult> Handle(CancelOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null)
        {
            _logger.LogWarning("CancelOrder failed: Order {OrderId} was not found.", request.OrderId);
            return CancelOrderResult.NotFound();
        }

        // Customer isolation / RBAC
        if (!request.IsAdmin && !string.Equals(order.CustomerId, request.CustomerId, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("CancelOrder forbidden: Customer '{CustomerId}' attempted to cancel Order '{OrderId}' owned by '{OwnerId}'.",
                request.CustomerId, request.OrderId, order.CustomerId);
            return CancelOrderResult.Forbidden();
        }

        try
        {
            order.Cancel();
        }
        catch (OrderingDomainException ex)
        {
            _logger.LogWarning(ex, "CancelOrder rejected for Order {OrderId}: {Message}", request.OrderId, ex.Message);
            return CancelOrderResult.InvalidState(ex.Message);
        }

        await _orderRepository.UpdateAsync(order, cancellationToken);
        await _orderRepository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Order {OrderId} was successfully cancelled.", order.Id);

        var response = MapToOrderResponse(order);
        return CancelOrderResult.Success(response);
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

