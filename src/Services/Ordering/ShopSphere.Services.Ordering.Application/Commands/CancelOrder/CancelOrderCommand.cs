using MediatR;
using ShopSphere.Services.Ordering.Application.DTOs;

namespace ShopSphere.Services.Ordering.Application.Commands.CancelOrder;

public record CancelOrderResult(
    OrderResponse? Order,
    bool IsSuccess,
    bool IsNotFound,
    bool IsForbidden,
    string? ErrorMessage
)
{
    public static CancelOrderResult Success(OrderResponse order) => new(order, true, false, false, null);
    public static CancelOrderResult NotFound() => new(null, false, true, false, "Order not found.");
    public static CancelOrderResult Forbidden() => new(null, false, false, true, "Forbidden: You do not own this order.");
    public static CancelOrderResult InvalidState(string message) => new(null, false, false, false, message);
}

public record CancelOrderCommand(
    Guid OrderId,
    string CustomerId,
    bool IsAdmin
) : IRequest<CancelOrderResult>;

