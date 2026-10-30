using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using ShopSphere.Services.Inventory.Models;
using ShopSphere.Services.Inventory.Tests.Fixtures;
using Xunit;

namespace ShopSphere.Services.Inventory.Tests;

public class LivePostgreSqlInventoryTests : IClassFixture<LivePostgreSqlInventoryWebApplicationFactory>
{
    private readonly HttpClient _client;

    public LivePostgreSqlInventoryTests(LivePostgreSqlInventoryWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task LivePostgres_GetStock_ReturnsSeededStockFromPostgresContainer()
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
}

