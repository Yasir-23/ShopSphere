using MediatR;
using Microsoft.Extensions.Logging;
using ShopSphere.Services.Ordering.Application.DTOs;
using ShopSphere.Services.Ordering.Application.Repositories;
using ShopSphere.Services.Ordering.Domain.Entities;

namespace ShopSphere.Services.Ordering.Application.Queries.GetOrdersByCustomer;

public class GetOrdersByCustomerQueryHandler : IRequestHandler<GetOrdersByCustomerQuery, PagedResult<OrderResponse>>
{
    private readonly IOrderRepository _orderRepository;
    private readonly ILogger<GetOrdersByCustomerQueryHandler> _logger;

    public GetOrdersByCustomerQueryHandler(
        IOrderRepository orderRepository,
        ILogger<GetOrdersByCustomerQueryHandler> logger)
    {
        _orderRepository = orderRepository;
        _logger = logger;
    }

    public async Task<PagedResult<OrderResponse>> Handle(GetOrdersByCustomerQuery request, CancellationToken cancellationToken)
    {
        var pageNumber = request.PageNumber <= 0 ? 1 : request.PageNumber;
        var pageSize = request.PageSize <= 0 ? 10 : (request.PageSize > 100 ? 100 : request.PageSize);

        var (orders, totalCount) = await _orderRepository.GetByCustomerIdAsync(
            request.CustomerId,
            pageNumber,
            pageSize,
            cancellationToken);

        _logger.LogInformation("Retrieved {Count} orders for customer {CustomerId} (Page {Page}, Size {Size}).",
            orders.Count, request.CustomerId, pageNumber, pageSize);

        var mapped = orders.Select(MapToOrderResponse).ToList();
        return PagedResult<OrderResponse>.Create(mapped, totalCount, pageNumber, pageSize);
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

