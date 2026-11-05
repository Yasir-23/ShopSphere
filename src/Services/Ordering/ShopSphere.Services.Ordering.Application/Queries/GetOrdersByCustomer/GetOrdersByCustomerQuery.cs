using MediatR;
using ShopSphere.Services.Ordering.Application.DTOs;

namespace ShopSphere.Services.Ordering.Application.Queries.GetOrdersByCustomer;

public record GetOrdersByCustomerQuery(
    string CustomerId,
    int PageNumber = 1,
    int PageSize = 10
) : IRequest<PagedResult<OrderResponse>>;

