using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using ShopSphere.Services.Inventory.Models;
using ShopSphere.Services.Inventory.Tests.Fixtures;
using ShopSphere.Services.Inventory.Tests.Helpers;
using Xunit;

namespace ShopSphere.Services.Inventory.Tests;

/// <summary>
/// Stock record provisioning for new catalog products, signed adjustments, and concurrency safety.
/// </summary>
public class StockProvisioningTests : IClassFixture<InventoryWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly string _adminToken = JwtTestHelper.GenerateToken(Guid.NewGuid().ToString(), role: "Admin");
    private readonly string _customerToken = JwtTestHelper.GenerateToken(Guid.NewGuid().ToString(), role: "Customer");

    public StockProvisioningTests(InventoryWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string? token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (body is not null) request.Content = JsonContent.Create(body);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _client.SendAsync(request);
    }

    private static string NewProductId() => Guid.NewGuid().ToString("N")[..24];

    [Fact]
    public async Task CreateStockItem_Admin_ProvisionsReadableStockRecord()
    {
        var productId = NewProductId();
        var response = await SendAsync(HttpMethod.Post, "/api/v1/inventory/items", _adminToken,
            new CreateStockItemRequest(productId, $"new-sku-{productId}", 50, 8));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<InventoryStockResponse>();
        created!.ProductId.Should().Be(productId);
        created.Sku.Should().Be($"NEW-SKU-{productId}".ToUpperInvariant());
        created.QuantityOnHand.Should().Be(50);
        created.QuantityAvailable.Should().Be(50);
        created.RestockThreshold.Should().Be(8);

        var read = await _client.GetFromJsonAsync<InventoryStockResponse>($"/api/v1/inventory/{productId}");
        read!.QuantityAvailable.Should().Be(50);
    }

    [Fact]
    public async Task CreateStockItem_Duplicate_Returns409_AndRequiresAdmin()
    {
        var productId = NewProductId();
        var body = new CreateStockItemRequest(productId, $"DUP-{productId}", 5);

        (await SendAsync(HttpMethod.Post, "/api/v1/inventory/items", null, body)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await SendAsync(HttpMethod.Post, "/api/v1/inventory/items", _customerToken, body)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Post, "/api/v1/inventory/items", _adminToken, body)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await SendAsync(HttpMethod.Post, "/api/v1/inventory/items", _adminToken, body)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await SendAsync(HttpMethod.Post, "/api/v1/inventory/items", _adminToken,
            new CreateStockItemRequest(productId, "X", -1))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AdjustStock_NegativeDeduction_CannotDropBelowReservedUnits()
    {
        var productId = NewProductId();
        await SendAsync(HttpMethod.Post, "/api/v1/inventory/items", _adminToken, new CreateStockItemRequest(productId, $"ADJ-{productId}", 10));

        // Reserve 6 units for an order
        var reserve = await SendAsync(HttpMethod.Post, "/api/v1/inventory/reserve", _customerToken,
            new ReserveStockRequest(Guid.NewGuid(), new List<StockReservationItem> { new(productId, 6) }));
        reserve.StatusCode.Should().Be(HttpStatusCode.OK);

        // Removing 3 leaves 7 on hand ≥ 6 reserved: allowed
        var ok = await SendAsync(HttpMethod.Post, "/api/v1/inventory/adjust", _adminToken, new AdjustStockRequest(null, productId, -3));
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        var afterDeduction = await ok.Content.ReadFromJsonAsync<InventoryStockResponse>();
        afterDeduction!.QuantityOnHand.Should().Be(7);
        afterDeduction.QuantityAvailable.Should().Be(1);

        // Removing 2 more would leave 5 on hand < 6 reserved: refused
        var refused = await SendAsync(HttpMethod.Post, "/api/v1/inventory/adjust", _adminToken, new AdjustStockRequest(null, productId, -2));
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Restock alias stays additions-only
        var negativeRestock = await SendAsync(HttpMethod.Post, "/api/v1/inventory/restock", _adminToken, new AdjustStockRequest(null, productId, -1));
        negativeRestock.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Stock with open reservations cannot be deleted
        (await SendAsync(HttpMethod.Delete, $"/api/v1/inventory/items/{productId}", _adminToken)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task AdjustStock_ConcurrentAdditions_AreNotLost()
    {
        var productId = NewProductId();
        await SendAsync(HttpMethod.Post, "/api/v1/inventory/items", _adminToken, new CreateStockItemRequest(productId, $"CONC-{productId}", 0));

        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ =>
            SendAsync(HttpMethod.Post, "/api/v1/inventory/adjust", _adminToken, new AdjustStockRequest(null, productId, 1))));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);
        var final = await _client.GetFromJsonAsync<InventoryStockResponse>($"/api/v1/inventory/{productId}");
        final!.QuantityOnHand.Should().Be(20);
    }

    [Fact]
    public async Task DeleteStockItem_RemovesRecord()
    {
        var productId = NewProductId();
        await SendAsync(HttpMethod.Post, "/api/v1/inventory/items", _adminToken, new CreateStockItemRequest(productId, $"DEL-{productId}", 3));

        (await SendAsync(HttpMethod.Delete, $"/api/v1/inventory/items/{productId}", _customerToken)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Delete, $"/api/v1/inventory/items/{productId}", _adminToken)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _client.GetAsync($"/api/v1/inventory/{productId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await SendAsync(HttpMethod.Delete, $"/api/v1/inventory/items/{productId}", _adminToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
