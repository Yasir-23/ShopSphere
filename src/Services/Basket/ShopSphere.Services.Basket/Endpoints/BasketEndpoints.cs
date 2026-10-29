using System.Security.Claims;
using Carter;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using ShopSphere.Common.Security;
using ShopSphere.Services.Basket.Models;
using ShopSphere.Services.Basket.Repositories;
using ShopSphere.Services.Basket.Services;

namespace ShopSphere.Services.Basket.Endpoints;

public class BasketEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/basket")
            .WithTags("Basket")
            .RequireAuthorization();

        // 1. Get Customer Cart
        group.MapGet("/", async (
            ClaimsPrincipal user,
            IBasketRepository repository) =>
        {
            var customerId = GetCustomerId(user);
            var basket = await repository.GetBasketAsync(customerId);

            if (basket is null)
            {
                basket = new ShoppingCart(customerId);
            }

            return Results.Ok(ToResponse(basket));
        })
        .WithName("GetCustomerBasket")
        .Produces<CustomerBasketResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized);

        // 2. Upsert Cart Items with gRPC Price Validation
        group.MapPost("/", async (
            ClaimsPrincipal user,
            [FromBody] UpsertBasketRequest request,
            IValidator<UpsertBasketRequest> validator,
            IBasketRepository repository,
            IPriceValidationService priceService,
            CancellationToken cancellationToken) =>
        {
            var validationResult = await validator.ValidateAsync(request, cancellationToken);
            if (!validationResult.IsValid)
            {
                return Results.ValidationProblem(validationResult.ToDictionary());
            }

            var customerId = GetCustomerId(user);

            // Synchronous Price Verification over gRPC
            var productIds = request.Items.Select(i => i.ProductId).Distinct().ToList();
            var priceMap = await priceService.ValidatePricesAsync(productIds, cancellationToken);

            var shoppingCartItems = new List<ShoppingCartItem>();

            foreach (var item in request.Items)
            {
                if (!priceMap.TryGetValue(item.ProductId, out var catalogItem) || !catalogItem.IsAvailable)
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "Product Unavailable",
                        detail: $"Product '{item.ProductName}' ({item.ProductId}) is unavailable or not found in the catalog.",
                        instance: "/api/v1/basket");
                }

                // Overwrite any client-supplied unit price with verified catalog price
                var verifiedPrice = (decimal)catalogItem.Price;

                shoppingCartItems.Add(new ShoppingCartItem
                {
                    ProductId = item.ProductId,
                    ProductName = item.ProductName,
                    UnitPrice = verifiedPrice,
                    Quantity = item.Quantity
                });
            }

            var basket = new ShoppingCart(customerId)
            {
                Items = shoppingCartItems,
                UpdatedAtUtc = DateTime.UtcNow
            };

            await repository.UpdateBasketAsync(basket);

            return Results.Ok(ToResponse(basket));
        })
        .WithName("UpsertBasket")
        .Produces<CustomerBasketResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized);

        // 3. Clear Customer Cart
        group.MapDelete("/", async (
            ClaimsPrincipal user,
            IBasketRepository repository) =>
        {
            var customerId = GetCustomerId(user);
            await repository.DeleteBasketAsync(customerId);
            return Results.NoContent();
        })
        .WithName("DeleteBasket")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status401Unauthorized);

        // 4. Checkout Basket
        group.MapPost("/checkout", async (
            ClaimsPrincipal user,
            [FromBody] CheckoutBasketRequest request,
            IValidator<CheckoutBasketRequest> validator,
            IBasketRepository repository,
            IPriceValidationService priceService,
            CancellationToken cancellationToken) =>
        {
            var validationResult = await validator.ValidateAsync(request, cancellationToken);
            if (!validationResult.IsValid)
            {
                return Results.ValidationProblem(validationResult.ToDictionary());
            }

            var customerId = GetCustomerId(user);
            var basket = await repository.GetBasketAsync(customerId);

            if (basket is null || basket.Items.Count == 0)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Empty Basket",
                    detail: "Cannot proceed to checkout with an empty basket.",
                    instance: "/api/v1/basket/checkout");
            }

            // Verify live pricing before checkout (SRS-BSK-03 / SDD 4.4)
            var productIds = basket.Items.Select(i => i.ProductId).Distinct().ToList();
            var priceMap = await priceService.ValidatePricesAsync(productIds, cancellationToken);

            var priceDiscrepancy = false;

            foreach (var item in basket.Items)
            {
                if (!priceMap.TryGetValue(item.ProductId, out var livePrice) || !livePrice.IsAvailable)
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "Product Unavailable",
                        detail: $"Product '{item.ProductName}' is no longer available in the catalog.",
                        instance: "/api/v1/basket/checkout");
                }

                var canonicalPrice = (decimal)livePrice.Price;
                if (item.UnitPrice != canonicalPrice)
                {
                    item.UnitPrice = canonicalPrice;
                    priceDiscrepancy = true;
                }
            }

            if (priceDiscrepancy)
            {
                // Update cart with refreshed prices and notify client with 409 Conflict
                await repository.UpdateBasketAsync(basket);

                return Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Price Discrepancy Detected",
                    detail: "One or more item prices have changed. Your cart has been updated with current catalog pricing. Please review before proceeding.",
                    instance: "/api/v1/basket/checkout");
            }

            // Clear basket from Redis upon successful checkout initiation
            var finalTotal = basket.TotalAmount;
            var finalCount = basket.Items.Sum(i => i.Quantity);
            await repository.DeleteBasketAsync(customerId);

            var summary = new CheckoutSummaryResponse(
                CheckoutId: Guid.NewGuid().ToString(),
                CustomerId: customerId,
                TotalAmount: finalTotal,
                ItemCount: finalCount,
                ShippingAddress: request.ShippingAddress,
                Status: "CheckoutInitiated",
                CreatedAtUtc: DateTime.UtcNow);

            return Results.Ok(summary);
        })
        .WithName("CheckoutBasket")
        .Produces<CheckoutSummaryResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status401Unauthorized);
    }

    private static string GetCustomerId(ClaimsPrincipal user)
    {
        return user.GetUserId()?.ToString()
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value
            ?? user.FindFirst("userId")?.Value
            ?? throw new InvalidOperationException("User identity is not authenticated or lacks a valid customer identifier.");
    }

    private static CustomerBasketResponse ToResponse(ShoppingCart basket) =>
        new(
            CustomerId: basket.CustomerId,
            Items: basket.Items.Select(i => new BasketItemResponse(
                ProductId: i.ProductId,
                ProductName: i.ProductName,
                UnitPrice: i.UnitPrice,
                Quantity: i.Quantity,
                TotalPrice: i.TotalPrice
            )).ToList(),
            TotalAmount: basket.TotalAmount);
}
