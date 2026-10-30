using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using ShopSphere.Services.Inventory.Models;
using ShopSphere.Services.Inventory.Tests.Fixtures;
using ShopSphere.Services.Inventory.Tests.Helpers;
using Xunit;

namespace ShopSphere.Services.Inventory.Tests;

public class InventoryIntegrationTests : IClassFixture<InventoryWebApplicationFactory>
{
    private readonly HttpClient _client;

    public InventoryIntegrationTests(InventoryWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Test1_GetStock_ExistingProduct_ReturnsSeededStockLevel()
    {
        // Arrange
        const string productId = "660c1d2e5a4b3c2d1e0f1001";

        // Act
        var response = await _client.GetAsync($"/api/v1/inventory/{productId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var stock = await response.Content.ReadFromJsonAsync<InventoryStockResponse>();
        stock.Should().NotBeNull();
        stock!.ProductId.Should().Be(productId);
        stock.Sku.Should().Be("KB-WL-RED-001");
        stock.QuantityOnHand.Should().Be(100);
        stock.QuantityReserved.Should().Be(0);
        stock.QuantityAvailable.Should().Be(100);
        stock.RestockThreshold.Should().Be(10);
    }

    [Fact]
    public async Task Test2_ReserveStock_ValidRequest_DecrementsAvailableStock()
    {
        // Arrange
        const string productId = "660c1d2e5a4b3c2d1e0f1002";
        var orderId = Guid.NewGuid();
        var customerToken = JwtTestHelper.GenerateToken(Guid.NewGuid().ToString(), role: "Customer");

        var reserveRequest = new ReserveStockRequest(
            orderId,
            new List<StockReservationItem>
            {
                new(productId, 15)
            });

        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/v1/inventory/reserve")
        {
            Content = JsonContent.Create(reserveRequest)
        };
        requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);

        // Act
        var reserveResponse = await _client.SendAsync(requestMessage);

        // Assert
        reserveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await reserveResponse.Content.ReadFromJsonAsync<StockReservationResult>();
        result.Should().NotBeNull();
        result!.IsSuccess.Should().BeTrue();
        result.Status.Should().Be("Reserved");

        // Verify updated stock level
        var stockResponse = await _client.GetAsync($"/api/v1/inventory/{productId}");
        stockResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var stock = await stockResponse.Content.ReadFromJsonAsync<InventoryStockResponse>();
        stock.Should().NotBeNull();
        stock!.QuantityOnHand.Should().Be(75);
        stock.QuantityReserved.Should().Be(15);
        stock.QuantityAvailable.Should().Be(60);
    }

    [Fact]
    public async Task Test3_ReserveStock_ExceedingAvailableQuantity_ReturnsConflict()
    {
        // Arrange
        const string productId = "660c1d2e5a4b3c2d1e0f1003";
        var orderId = Guid.NewGuid();
        var customerToken = JwtTestHelper.GenerateToken(Guid.NewGuid().ToString(), role: "Customer");

        var reserveRequest = new ReserveStockRequest(
            orderId,
            new List<StockReservationItem>
            {
                new(productId, 9999) // Far exceeds available stock (80)
            });

        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/v1/inventory/reserve")
        {
            Content = JsonContent.Create(reserveRequest)
        };
        requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);

        // Act
        var reserveResponse = await _client.SendAsync(requestMessage);

        // Assert
        reserveResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Test4_ReleaseStock_ValidOrder_RestoresAvailableStock()
    {
        // Arrange
        const string productId = "660c1d2e5a4b3c2d1e0f1004";
        var orderId = Guid.NewGuid();
        var customerToken = JwtTestHelper.GenerateToken(Guid.NewGuid().ToString(), role: "Customer");

        // Step 1: Reserve 20 units
        var reserveRequest = new ReserveStockRequest(
            orderId,
            new List<StockReservationItem>
            {
                new(productId, 20)
            });

        using (var resMsg = new HttpRequestMessage(HttpMethod.Post, "/api/v1/inventory/reserve")
        {
            Content = JsonContent.Create(reserveRequest)
        })
        {
            resMsg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);
            var resResponse = await _client.SendAsync(resMsg);
            resResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // Verify reserved state
        var midStock = await _client.GetFromJsonAsync<InventoryStockResponse>($"/api/v1/inventory/{productId}");
        midStock.Should().NotBeNull();
        midStock!.QuantityOnHand.Should().Be(60);
        midStock.QuantityReserved.Should().Be(20);
        midStock.QuantityAvailable.Should().Be(40);

        // Step 2: Release reservation
        var releaseRequest = new ReleaseStockRequest(orderId);
        using (var relMsg = new HttpRequestMessage(HttpMethod.Post, "/api/v1/inventory/release")
        {
            Content = JsonContent.Create(releaseRequest)
        })
        {
            relMsg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);
            var relResponse = await _client.SendAsync(relMsg);
            relResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // Assert: stock returned back to available pool
        var finalStock = await _client.GetFromJsonAsync<InventoryStockResponse>($"/api/v1/inventory/{productId}");
        finalStock.Should().NotBeNull();
        finalStock!.QuantityOnHand.Should().Be(60);
        finalStock.QuantityReserved.Should().Be(0);
        finalStock.QuantityAvailable.Should().Be(60);
    }

    [Fact]
    public async Task Test5_AdjustStock_SecurityEnforcement_RequiresAdministratorOrAdminRole()
    {
        // Arrange
        var adjustPayload = new AdjustStockRequest(
            Sku: "DK-TB4-PRO-005",
            ProductId: null,
            Quantity: 25,
            Reason: "Shipment Received");

        // 1. Anonymous request without token -> 401 Unauthorized
        var anonResponse = await _client.PostAsJsonAsync("/api/v1/inventory/adjust", adjustPayload);
        anonResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // 2. Customer token -> 403 Forbidden
        var customerToken = JwtTestHelper.GenerateToken(Guid.NewGuid().ToString(), role: "Customer");
        using (var custMsg = new HttpRequestMessage(HttpMethod.Post, "/api/v1/inventory/adjust")
        {
            Content = JsonContent.Create(adjustPayload)
        })
        {
            custMsg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);
            var custResponse = await _client.SendAsync(custMsg);
            custResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        // 3. Administrator token -> 200 OK
        var adminToken = JwtTestHelper.GenerateToken(Guid.NewGuid().ToString(), role: "Administrator");
        using (var adminMsg = new HttpRequestMessage(HttpMethod.Post, "/api/v1/inventory/adjust")
        {
            Content = JsonContent.Create(adjustPayload)
        })
        {
            adminMsg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            var adminResponse = await _client.SendAsync(adminMsg);
            adminResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var adjustedStock = await adminResponse.Content.ReadFromJsonAsync<InventoryStockResponse>();
            adjustedStock.Should().NotBeNull();
            adjustedStock!.Sku.Should().Be("DK-TB4-PRO-005");
            adjustedStock.QuantityOnHand.Should().Be(75); // Seeded 50 + 25 = 75
            adjustedStock.QuantityAvailable.Should().Be(75);
        }
    }
}

