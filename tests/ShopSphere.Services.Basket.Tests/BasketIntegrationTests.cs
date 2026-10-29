using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using ShopSphere.Services.Basket.Models;
using ShopSphere.Services.Basket.Tests.Fixtures;
using ShopSphere.Services.Basket.Tests.Helpers;
using ShopSphere.Services.Catalog.Grpc;
using Xunit;

namespace ShopSphere.Services.Basket.Tests;

public class BasketIntegrationTests : IClassFixture<BasketWebApplicationFactory>
{
    private readonly BasketWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public BasketIntegrationTests(BasketWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetBasket_WithoutToken_ReturnsUnauthorized()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/basket");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetBasket_WithValidToken_ReturnsEmptyBasket()
    {
        // Arrange
        var customerId = Guid.NewGuid().ToString();
        var token = JwtTestHelper.GenerateToken(customerId);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/basket");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var basket = await response.Content.ReadFromJsonAsync<CustomerBasketResponse>();
        basket.Should().NotBeNull();
        basket!.CustomerId.Should().Be(customerId);
        basket.Items.Should().BeEmpty();
        basket.TotalAmount.Should().Be(0m);
    }

    [Fact]
    public async Task PostBasket_WithItems_OverridesClientPricesWithCanonicalPrices()
    {
        // Arrange
        var customerId = Guid.NewGuid().ToString();
        var token = JwtTestHelper.GenerateToken(customerId);

        const string productId1 = "660c1d2e5a4b3c2d1e0f1001";
        const string productId2 = "660c1d2e5a4b3c2d1e0f1003";

        // Setup gRPC mock to return canonical prices: $129.99 for product 1 and $89.99 for product 2
        _factory.SetupCanonicalPrices(
            new ProductPriceItem
            {
                ProductId = productId1,
                Price = 129.99,
                Currency = "USD",
                IsAvailable = true
            },
            new ProductPriceItem
            {
                ProductId = productId2,
                Price = 89.99,
                Currency = "USD",
                IsAvailable = true
            });

        // Client payload attempting to set $1.00 unit prices
        var requestPayload = new UpsertBasketRequest(
            Items: new List<UpsertBasketItemRequest>
            {
                new(
                    ProductId: productId1,
                    ProductName: "Smart Noise-Cancelling Headphones",
                    UnitPrice: 1.00m,
                    Quantity: 2),
                new(
                    ProductId: productId2,
                    ProductName: "Ultra-Slim Power Bank 20000mAh",
                    UnitPrice: 1.00m,
                    Quantity: 1)
            });

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/basket");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(requestPayload);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var basket = await response.Content.ReadFromJsonAsync<CustomerBasketResponse>();
        basket.Should().NotBeNull();
        basket!.CustomerId.Should().Be(customerId);
        basket.Items.Should().HaveCount(2);

        // Verify Item 1: Client $1.00 was overridden by canonical $129.99 (Quantity 2 -> Total $259.98)
        var item1 = basket.Items.Single(i => i.ProductId == productId1);
        item1.UnitPrice.Should().Be(129.99m);
        item1.Quantity.Should().Be(2);
        item1.TotalPrice.Should().Be(259.98m);

        // Verify Item 2: Client $1.00 was overridden by canonical $89.99 (Quantity 1 -> Total $89.99)
        var item2 = basket.Items.Single(i => i.ProductId == productId2);
        item2.UnitPrice.Should().Be(89.99m);
        item2.Quantity.Should().Be(1);
        item2.TotalPrice.Should().Be(89.99m);

        // Total amount must equal $349.97 exactly
        basket.TotalAmount.Should().Be(349.97m);
    }

    [Fact]
    public async Task PostBasket_WhenProductUnavailable_ReturnsBadRequest()
    {
        // Arrange
        var customerId = Guid.NewGuid().ToString();
        var token = JwtTestHelper.GenerateToken(customerId);

        const string unavailableProductId = "660c1d2e5a4b3c2d1e0f9999";

        // Setup gRPC mock to return product with is_available = false
        _factory.SetupCanonicalPrices(
            new ProductPriceItem
            {
                ProductId = unavailableProductId,
                Price = 50.00,
                Currency = "USD",
                IsAvailable = false
            });

        var requestPayload = new UpsertBasketRequest(
            Items: new List<UpsertBasketItemRequest>
            {
                new(
                    ProductId: unavailableProductId,
                    ProductName: "Discontinued Gadget",
                    UnitPrice: 50.00m,
                    Quantity: 1)
            });

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/basket");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(requestPayload);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
