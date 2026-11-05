using MediatR;
using ShopSphere.Services.Ordering.Application.DTOs;

namespace ShopSphere.Services.Ordering.Application.Commands.CreateOrder;

public record CreateOrderItemDto(
    string ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity
);

public record CreateOrderCommand(
    string CustomerId,
    string CustomerEmail,
    AddressDto ShippingAddress,
    List<CreateOrderItemDto> Items
) : IRequest<OrderResponse>;

