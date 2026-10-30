using Carter;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShopSphere.Services.Inventory.Models;
using ShopSphere.Services.Inventory.Services;

namespace ShopSphere.Services.Inventory.Endpoints;

public class InventoryEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/inventory")
            .WithTags("Inventory");

        // 1. Check Stock Level
        group.MapGet("/{productId}", async (
            string productId,
            IInventoryService inventoryService,
            CancellationToken cancellationToken) =>
        {
            var stock = await inventoryService.GetStockAsync(productId, cancellationToken);
            if (stock is null)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Stock Not Found",
                    detail: $"No inventory item found for identifier '{productId}'.");
            }

            return Results.Ok(stock);
        })
        .WithName("GetInventoryStock")
        .Produces<InventoryStockResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // 2. Reserve Stock
        group.MapPost("/reserve", async (
            [FromBody] ReserveStockRequest request,
            IValidator<ReserveStockRequest> validator,
            IInventoryService inventoryService,
            CancellationToken cancellationToken) =>
        {
            var validationResult = await validator.ValidateAsync(request, cancellationToken);
            if (!validationResult.IsValid)
            {
                return Results.ValidationProblem(validationResult.ToDictionary());
            }

            var result = await inventoryService.ReserveStockAsync(request, cancellationToken);
            if (!result.IsSuccess)
            {
                if (result.StatusCode == StatusCodes.Status404NotFound)
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status404NotFound,
                        title: "Product Not Found",
                        detail: result.ErrorMessage);
                }

                return Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Stock Reservation Conflict",
                    detail: result.ErrorMessage);
            }

            return Results.Ok(result);
        })
        .WithName("ReserveStock")
        .RequireAuthorization()
        .Produces<StockReservationResult>(StatusCodes.Status200OK)
        .ProducesValidationProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        // 3. Release Stock Reservation
        group.MapPost("/release", async (
            [FromBody] ReleaseStockRequest request,
            IValidator<ReleaseStockRequest> validator,
            IInventoryService inventoryService,
            CancellationToken cancellationToken) =>
        {
            var validationResult = await validator.ValidateAsync(request, cancellationToken);
            if (!validationResult.IsValid)
            {
                return Results.ValidationProblem(validationResult.ToDictionary());
            }

            var result = await inventoryService.ReleaseStockAsync(request, cancellationToken);
            if (!result.IsSuccess)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Release Stock Failed",
                    detail: result.ErrorMessage);
            }

            return Results.Ok(result);
        })
        .WithName("ReleaseStock")
        .RequireAuthorization()
        .Produces<StockReleaseResult>(StatusCodes.Status200OK)
        .ProducesValidationProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized);

        // 4. Commit Stock Deduction
        group.MapPost("/commit", async (
            [FromBody] CommitStockRequest request,
            IInventoryService inventoryService,
            CancellationToken cancellationToken) =>
        {
            if (request.OrderId == Guid.Empty)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Invalid Request",
                    detail: "OrderId must be specified.");
            }

            var result = await inventoryService.CommitStockAsync(request, cancellationToken);
            if (!result.IsSuccess)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Commit Stock Failed",
                    detail: result.ErrorMessage);
            }

            return Results.Ok(result);
        })
        .WithName("CommitStock")
        .RequireAuthorization()
        .Produces<StockCommitResult>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized);

        // 5. Adjust Stock (Admin Only: Supports Administrator and Admin roles)
        group.MapPost("/adjust", async (
            [FromBody] AdjustStockRequest request,
            IValidator<AdjustStockRequest> validator,
            IInventoryService inventoryService,
            CancellationToken cancellationToken) =>
        {
            var validationResult = await validator.ValidateAsync(request, cancellationToken);
            if (!validationResult.IsValid)
            {
                return Results.ValidationProblem(validationResult.ToDictionary());
            }

            var stock = await inventoryService.AdjustStockAsync(request, cancellationToken);
            if (stock is null)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Stock Item Not Found",
                    detail: $"Stock item with SKU '{request.Sku}' or ProductId '{request.ProductId}' was not found.");
            }

            return Results.Ok(stock);
        })
        .WithName("AdjustStock")
        .RequireAuthorization(new AuthorizeAttribute { Roles = "Administrator,Admin" })
        .Produces<InventoryStockResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // 6. Restock Inventory (API_SPEC 6.2 Alias for AdjustStock)
        group.MapPost("/restock", async (
            [FromBody] AdjustStockRequest request,
            IValidator<AdjustStockRequest> validator,
            IInventoryService inventoryService,
            CancellationToken cancellationToken) =>
        {
            var validationResult = await validator.ValidateAsync(request, cancellationToken);
            if (!validationResult.IsValid)
            {
                return Results.ValidationProblem(validationResult.ToDictionary());
            }

            var stock = await inventoryService.AdjustStockAsync(request, cancellationToken);
            if (stock is null)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Stock Item Not Found",
                    detail: $"Stock item with SKU '{request.Sku}' was not found.");
            }

            return Results.Ok(stock);
        })
        .WithName("RestockInventory")
        .RequireAuthorization(new AuthorizeAttribute { Roles = "Administrator,Admin" })
        .Produces<InventoryStockResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}

