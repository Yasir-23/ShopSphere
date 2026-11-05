using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using ShopSphere.Services.Ordering.Api.Endpoints;
using ShopSphere.Services.Ordering.Application.DTOs;
using ShopSphere.Services.Ordering.Application.Commands.CreateOrder;
using ShopSphere.Services.Ordering.Tests.Fixtures;
using ShopSphere.Services.Ordering.Tests.Helpers;
using Xunit;

namespace ShopSphere.Services.Ordering.Tests;

public class OrderingIntegrationTests : IClassFixture<OrderingWebApplicationFactory>
{
    private readonly OrderingWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public OrderingIntegrationTests(OrderingWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static CreateOrderRequest CreateSampleOrderRequest()
    {
        return new CreateOrderRequest(
            ShippingAddress: new AddressDto(
                Street: "123 Tech Boulevard",
                City: "Seattle",
                State: "WA",
                ZipCode: "98101",
                Country: "USA"),
            Items: new List<CreateOrderItemDto>
            {
                new(
                    ProductId: "660c1d2e5a4b3c2d1e0f1001",
                    ProductName: "Wireless Mechanical Keyboard",
                    UnitPrice: 89.99m,
                    Quantity: 2),
                new(
                    ProductId: "660c1d2e5a4b3c2d1e0f1002",
                    ProductName: "USB-C Fast Charging Cable",
                    UnitPrice: 15.00m,
                    Quantity: 1)
            });
    }

    [Fact]
    public async Task Test1_AuthenticationRequirement_AnonymousPostOrder_ReturnsUnauthorized()
    {
        // Arrange
        var requestPayload = CreateSampleOrderRequest();

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/orders", requestPayload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Test2_OrderCreationWorkflow_AuthenticatedCaller_CreatesOrderAndReturnsCreated()
    {
        // Arrange
        var customerId = Guid.NewGuid().ToString();
        var customerEmail = "customer.alpha@shopsphere.io";
        var token = JwtTestHelper.GenerateToken(customerId, email: customerEmail);

        var requestPayload = CreateSampleOrderRequest();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders")
        {
            Content = JsonContent.Create(requestPayload)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();

        var order = await response.Content.ReadFromJsonAsync<OrderResponse>();
        order.Should().NotBeNull();
        order!.Id.Should().NotBeEmpty();
        order.CustomerId.Should().Be(customerId);
        order.CustomerEmail.Should().Be(customerEmail);
        order.Status.Should().Be("Pending");
        order.TotalAmount.Should().Be(194.98m); // (89.99 * 2) + (15.00 * 1) = 179.98 + 15.00 = 194.98
        order.Items.Should().HaveCount(2);

        var item1 = order.Items.First(i => i.ProductId == "660c1d2e5a4b3c2d1e0f1001");
        item1.Quantity.Should().Be(2);
        item1.UnitPrice.Should().Be(89.99m);
        item1.TotalPrice.Should().Be(179.98m);

        var item2 = order.Items.First(i => i.ProductId == "660c1d2e5a4b3c2d1e0f1002");
        item2.Quantity.Should().Be(1);
        item2.UnitPrice.Should().Be(15.00m);
        item2.TotalPrice.Should().Be(15.00m);
    }

    [Fact]
    public async Task Test3_OrderRetrievalById_ReturnsOrderDetailsWithItemsAndAddress()
    {
        // Arrange: Create order first
        var customerId = Guid.NewGuid().ToString();
        var token = JwtTestHelper.GenerateToken(customerId);
        var requestPayload = CreateSampleOrderRequest();

        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders")
        {
            Content = JsonContent.Create(requestPayload)
        };
        createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var createResponse = await _client.SendAsync(createRequest);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var createdOrder = await createResponse.Content.ReadFromJsonAsync<OrderResponse>();
        createdOrder.Should().NotBeNull();

        // Act: Retrieve created order by Id
        using var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/orders/{createdOrder!.Id}");
        getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var getResponse = await _client.SendAsync(getRequest);

        // Assert
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var retrievedOrder = await getResponse.Content.ReadFromJsonAsync<OrderResponse>();
        retrievedOrder.Should().NotBeNull();
        retrievedOrder!.Id.Should().Be(createdOrder.Id);
        retrievedOrder.CustomerId.Should().Be(customerId);
        retrievedOrder.Status.Should().Be("Pending");
        retrievedOrder.TotalAmount.Should().Be(194.98m);

        retrievedOrder.ShippingAddress.Should().NotBeNull();
        retrievedOrder.ShippingAddress.Street.Should().Be("123 Tech Boulevard");
        retrievedOrder.ShippingAddress.City.Should().Be("Seattle");
        retrievedOrder.ShippingAddress.State.Should().Be("WA");
        retrievedOrder.ShippingAddress.ZipCode.Should().Be("98101");
        retrievedOrder.ShippingAddress.Country.Should().Be("USA");

        retrievedOrder.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task Test4_CustomerIsolation_OtherCustomerCannotAccessOrder_ReturnsForbidden()
    {
        // Arrange: Customer A creates an order
        var customerAId = Guid.NewGuid().ToString();
        var tokenCustomerA = JwtTestHelper.GenerateToken(customerAId, email: "customer.a@shopsphere.io");

        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders")
        {
            Content = JsonContent.Create(CreateSampleOrderRequest())
        };
        createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenCustomerA);
        var createResponse = await _client.SendAsync(createRequest);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var createdOrder = await createResponse.Content.ReadFromJsonAsync<OrderResponse>();
        createdOrder.Should().NotBeNull();

        // Act: Customer B attempts to retrieve Customer A's order
        var customerBId = Guid.NewGuid().ToString();
        var tokenCustomerB = JwtTestHelper.GenerateToken(customerBId, email: "customer.b@shopsphere.io");

        using var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/orders/{createdOrder!.Id}");
        getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenCustomerB);
        var getResponse = await _client.SendAsync(getRequest);

        // Assert: Customer B is rejected with 403 Forbidden
        getResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Test5_OrderCancellation_PendingOrder_UpdatesStatusToCancelled()
    {
        // Arrange: Create a pending order
        var customerId = Guid.NewGuid().ToString();
        var token = JwtTestHelper.GenerateToken(customerId);

        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders")
        {
            Content = JsonContent.Create(CreateSampleOrderRequest())
        };
        createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var createResponse = await _client.SendAsync(createRequest);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var createdOrder = await createResponse.Content.ReadFromJsonAsync<OrderResponse>();
        createdOrder.Should().NotBeNull();
        createdOrder!.Status.Should().Be("Pending");

        // Act: Cancel the order via PUT /api/v1/orders/{id}/cancel
        using var cancelRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/orders/{createdOrder.Id}/cancel");
        cancelRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var cancelResponse = await _client.SendAsync(cancelRequest);

        // Assert: Cancellation returns 200 OK with updated status Cancelled
        cancelResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var cancelledOrder = await cancelResponse.Content.ReadFromJsonAsync<OrderResponse>();
        cancelledOrder.Should().NotBeNull();
        cancelledOrder!.Id.Should().Be(createdOrder.Id);
        cancelledOrder.Status.Should().Be("Cancelled");

        // Verify database state directly via GET
        using var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/orders/{createdOrder.Id}");
        getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var getResponse = await _client.SendAsync(getRequest);

        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var verifiedOrder = await getResponse.Content.ReadFromJsonAsync<OrderResponse>();
        verifiedOrder.Should().NotBeNull();
        verifiedOrder!.Status.Should().Be("Cancelled");
    }

    [Fact]
    public async Task Test6_AdminRole_CanAccessAnyCustomerOrder()
    {
        // Arrange: Customer creates an order
        var customerId = Guid.NewGuid().ToString();
        var customerToken = JwtTestHelper.GenerateToken(customerId);

        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders")
        {
            Content = JsonContent.Create(CreateSampleOrderRequest())
        };
        createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);
        var createResponse = await _client.SendAsync(createRequest);
        var createdOrder = await createResponse.Content.ReadFromJsonAsync<OrderResponse>();

        // Act: Administrator accesses the customer's order
        var adminToken = JwtTestHelper.GenerateToken(Guid.NewGuid().ToString(), role: "Administrator");
        using var adminGetRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/orders/{createdOrder!.Id}");
        adminGetRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var adminGetResponse = await _client.SendAsync(adminGetRequest);

        // Assert: 200 OK
        adminGetResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var order = await adminGetResponse.Content.ReadFromJsonAsync<OrderResponse>();
        order.Should().NotBeNull();
        order!.Id.Should().Be(createdOrder.Id);
    }

    [Fact]
    public async Task Test7_GetMyOrders_ReturnsPaginatedOrdersForCustomer()
    {
        // Arrange: Create 2 orders for the customer
        var customerId = Guid.NewGuid().ToString();
        var token = JwtTestHelper.GenerateToken(customerId);

        for (int i = 0; i < 2; i++)
        {
            using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders")
            {
                Content = JsonContent.Create(CreateSampleOrderRequest())
            };
            createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var res = await _client.SendAsync(createRequest);
            res.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        // Act: Query my orders
        using var myOrdersRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/orders/my-orders?pageNumber=1&pageSize=10");
        myOrdersRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var myOrdersResponse = await _client.SendAsync(myOrdersRequest);

        // Assert
        myOrdersResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var pagedResult = await myOrdersResponse.Content.ReadFromJsonAsync<PagedResult<OrderResponse>>();
        pagedResult.Should().NotBeNull();
        pagedResult!.TotalCount.Should().Be(2);
        pagedResult.Items.Should().HaveCount(2);
    }
}

