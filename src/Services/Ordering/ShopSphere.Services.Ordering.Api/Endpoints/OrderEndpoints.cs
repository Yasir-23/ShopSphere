using System.Security.Claims;
using Carter;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using ShopSphere.Common.Security;
using ShopSphere.Services.Ordering.Application.Commands.CancelOrder;
using ShopSphere.Services.Ordering.Application.Commands.CreateOrder;
using ShopSphere.Services.Ordering.Application.DTOs;
using ShopSphere.Services.Ordering.Application.Queries.GetOrderById;
using ShopSphere.Services.Ordering.Application.Queries.GetOrdersByCustomer;

namespace ShopSphere.Services.Ordering.Api.Endpoints;

public record CreateOrderRequest(
    AddressDto ShippingAddress,
    List<CreateOrderItemDto> Items,
    string? CustomerId = null,
    string? CustomerEmail = null,
    string? PaymentMethod = null
);

public class OrderEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/orders")
            .WithTags("Orders");

        // 1. Create Order
        group.MapPost("/", async (
            [FromBody] CreateOrderRequest request,
            ClaimsPrincipal user,
            IValidator<CreateOrderCommand> validator,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var customerId = user.GetUserId()?.ToString()
                ?? user.FindFirst("sub")?.Value
                ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? request.CustomerId;

            if (string.IsNullOrWhiteSpace(customerId))
            {
                return Results.Unauthorized();
            }

            var customerEmail = user.GetEmail()
                ?? request.CustomerEmail
                ?? "customer@shopsphere.local";

            var command = new CreateOrderCommand(
                customerId,
                customerEmail,
                request.ShippingAddress,
                request.Items ?? new List<CreateOrderItemDto>(),
                request.PaymentMethod);

            var validationResult = await validator.ValidateAsync(command, cancellationToken);
            if (!validationResult.IsValid)
            {
                return Results.ValidationProblem(validationResult.ToDictionary());
            }

            var order = await sender.Send(command, cancellationToken);
            return Results.Created($"/api/v1/orders/{order.Id}", order);
        })
        .WithName("CreateOrder")
        .RequireAuthorization()
        .Produces<OrderResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized);

        // 2. Get Order By Id
        group.MapGet("/{id:guid}", async (
            Guid id,
            ClaimsPrincipal user,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var customerId = user.GetUserId()?.ToString()
                ?? user.FindFirst("sub")?.Value
                ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrWhiteSpace(customerId))
            {
                return Results.Unauthorized();
            }

            var isAdmin = user.HasRole("Administrator") || user.HasRole("Admin");

            var query = new GetOrderByIdQuery(id, customerId, isAdmin);
            var result = await sender.Send(query, cancellationToken);

            if (result.IsNotFound)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Order Not Found",
                    detail: $"Order with ID '{id}' was not found.");
            }

            if (result.IsForbidden)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Forbidden",
                    detail: "You do not have permission to view this order.");
            }

            return Results.Ok(result.Order);
        })
        .WithName("GetOrderById")
        .RequireAuthorization()
        .Produces<OrderResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // 3. Get My Orders
        group.MapGet("/my-orders", async (
            ClaimsPrincipal user,
            ISender sender,
            [FromQuery] int? pageNumber,
            [FromQuery] int? pageSize,
            CancellationToken cancellationToken) =>
        {
            var customerId = user.GetUserId()?.ToString()
                ?? user.FindFirst("sub")?.Value
                ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrWhiteSpace(customerId))
            {
                return Results.Unauthorized();
            }

            var query = new GetOrdersByCustomerQuery(
                customerId,
                pageNumber ?? 1,
                pageSize ?? 10);

            var result = await sender.Send(query, cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetMyOrders")
        .RequireAuthorization()
        .Produces<PagedResult<OrderResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized);

        // 3b. Also support GET /api/v1/orders for compatibility with API_SPEC 5.1
        group.MapGet("/", async (
            ClaimsPrincipal user,
            ISender sender,
            [FromQuery] int? pageNumber,
            [FromQuery] int? pageSize,
            CancellationToken cancellationToken) =>
        {
            var customerId = user.GetUserId()?.ToString()
                ?? user.FindFirst("sub")?.Value
                ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrWhiteSpace(customerId))
            {
                return Results.Unauthorized();
            }

            var query = new GetOrdersByCustomerQuery(
                customerId,
                pageNumber ?? 1,
                pageSize ?? 10);

            var result = await sender.Send(query, cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetOrders")
        .RequireAuthorization()
        .Produces<PagedResult<OrderResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized);

        // 4. Cancel Order (PUT and POST)
        group.MapPut("/{id:guid}/cancel", CancelOrder)
            .WithName("CancelOrderPut")
            .RequireAuthorization()
            .Produces<OrderResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/cancel", CancelOrder)
            .WithName("CancelOrderPost")
            .RequireAuthorization()
            .Produces<OrderResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> CancelOrder(
        Guid id,
        ClaimsPrincipal user,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var customerId = user.GetUserId()?.ToString()
            ?? user.FindFirst("sub")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrWhiteSpace(customerId))
        {
            return Results.Unauthorized();
        }

        var isAdmin = user.HasRole("Administrator") || user.HasRole("Admin");

        var command = new CancelOrderCommand(id, customerId, isAdmin);
        var result = await sender.Send(command, cancellationToken);

        if (result.IsNotFound)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Order Not Found",
                detail: $"Order with ID '{id}' was not found.");
        }

        if (result.IsForbidden)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Forbidden",
                detail: "You do not have permission to cancel this order.");
        }

        if (!result.IsSuccess)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Order Cancellation Failed",
                detail: result.ErrorMessage);
        }

        return Results.Ok(result.Order);
    }
}

