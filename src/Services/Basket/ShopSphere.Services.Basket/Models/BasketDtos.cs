namespace ShopSphere.Services.Basket.Models;

public record CustomerBasketResponse(
    string CustomerId,
    List<BasketItemResponse> Items,
    decimal TotalAmount);

public record BasketItemResponse(
    string ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    decimal TotalPrice);

public record UpsertBasketRequest(
    List<UpsertBasketItemRequest> Items);

public record UpsertBasketItemRequest(
    string ProductId,
    string ProductName,
    decimal? UnitPrice,
    int Quantity);

public record ShippingAddressDto(
    string Street,
    string City,
    string State,
    string ZipCode,
    string Country);

public record CheckoutBasketRequest(
    ShippingAddressDto ShippingAddress);

public record CheckoutSummaryResponse(
    string CheckoutId,
    string CustomerId,
    decimal TotalAmount,
    int ItemCount,
    ShippingAddressDto ShippingAddress,
    string Status,
    DateTime CreatedAtUtc);
