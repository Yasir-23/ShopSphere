using MediatR;
using Microsoft.Extensions.Logging;
using ShopSphere.Services.Ordering.Application.DTOs;
using ShopSphere.Services.Ordering.Application.Repositories;
using ShopSphere.Services.Ordering.Domain.Entities;

namespace ShopSphere.Services.Ordering.Application.Queries.GetOrderById;

public class GetOrderByIdQueryHandler : IRequestHandler<GetOrderByIdQuery, OrderQueryResult>
{
    private readonly IOrderRepository _orderRepository;
    private readonly ILogger<GetOrderByIdQueryHandler> _logger;

    public GetOrderByIdQueryHandler(
        IOrderRepository orderRepository,
        ILogger<GetOrderByIdQueryHandler> logger)
    {
        _orderRepository = orderRepository;
        _logger = logger;
    }

    public async Task<OrderQueryResult> Handle(GetOrderByIdQuery request, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null)
        {
            _logger.LogWarning("Order {OrderId} was not found.", request.OrderId);
            return OrderQueryResult.NotFound();
        }

        // Customer isolation / RBAC: Assert customer owns the order or has Administrator role
        if (!request.IsAdmin && !string.Equals(order.CustomerId, request.CustomerId, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Forbidden access attempt: Customer '{CustomerId}' tried to access Order '{OrderId}' owned by '{OwnerId}'.",
                request.CustomerId, request.OrderId, order.CustomerId);
            return OrderQueryResult.Forbidden();
        }

        var response = MapToOrderResponse(order);
        return OrderQueryResult.Success(response);
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
        order.UpdatedAtUtc,
        order.CancellationReason);
}

