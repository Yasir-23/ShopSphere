using MediatR;
using ShopSphere.Services.Ordering.Application.DTOs;

namespace ShopSphere.Services.Ordering.Application.Queries.GetOrderById;

public record OrderQueryResult(
    OrderResponse? Order,
    bool IsForbidden,
    bool IsNotFound
)
{
    public static OrderQueryResult Success(OrderResponse order) => new(order, false, false);
    public static OrderQueryResult NotFound() => new(null, false, true);
    public static OrderQueryResult Forbidden() => new(null, true, false);
}

public record GetOrderByIdQuery(
    Guid OrderId,
    string CustomerId,
    bool IsAdmin
) : IRequest<OrderQueryResult>;

