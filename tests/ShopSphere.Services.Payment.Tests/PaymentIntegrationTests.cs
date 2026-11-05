using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using ShopSphere.EventBus.Events;
using ShopSphere.Services.Ordering.Application.Events;
using ShopSphere.Services.Payment.Consumers;
using ShopSphere.Services.Payment.Data;
using ShopSphere.Services.Payment.Entities;
using ShopSphere.Services.Payment.Models;
using ShopSphere.Services.Payment.Tests.Fixtures;
using ShopSphere.Services.Payment.Tests.Helpers;
using Xunit;

namespace ShopSphere.Services.Payment.Tests;

public class PaymentIntegrationTests : IClassFixture<PaymentWebApplicationFactory>
{
    private readonly PaymentWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly string _jwtToken;

    public PaymentIntegrationTests(PaymentWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _jwtToken = JwtTestHelper.GenerateToken(Guid.NewGuid().ToString(), "buyer@shopsphere.io", "Customer");
    }

    [Fact]
    public async Task PostPayment_Anonymous_Returns401Unauthorized()
    {
        // Arrange
        var request = new ProcessPaymentRequest(
            Guid.NewGuid(),
            100.00m,
            "USD",
            "CreditCard"
        );

        // Clear any auth header
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = null;

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/payments", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostPayment_SuccessfulExecution_TransitionsStatusToCompletedAndReturns200WithTransactionId()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var request = new ProcessPaymentRequest(
            orderId,
            149.99m,
            "USD",
            "Visa_4242"
        );

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _jwtToken);

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/payments", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var paymentResponse = await response.Content.ReadFromJsonAsync<PaymentResponse>();
        paymentResponse.Should().NotBeNull();
        paymentResponse!.OrderId.Should().Be(orderId);
        paymentResponse.Amount.Should().Be(149.99m);
        paymentResponse.Currency.Should().Be("USD");
        paymentResponse.Status.Should().Be("Completed");
        paymentResponse.TransactionId.Should().NotBeNullOrWhiteSpace();
        paymentResponse.TransactionId.Should().StartWith("ch_sim_");
        paymentResponse.FailureReason.Should().BeNull();

        // Verify state in database
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        var dbTxn = await db.Transactions.FirstOrDefaultAsync(t => t.OrderId == orderId);
        dbTxn.Should().NotBeNull();
        dbTxn!.Status.Should().Be(PaymentStatus.Completed);
        dbTxn.TransactionId.Should().Be(paymentResponse.TransactionId);

        // Verify EventBus published event
        _factory.MockPublishEndpoint.Verify(
            p => p.Publish(It.Is<PaymentProcessedIntegrationEvent>(e => e.OrderId == orderId), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task PostPayment_FailedPayment_NegativeAmount_Returns400BadRequest()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var request = new ProcessPaymentRequest(
            orderId,
            -25.00m,
            "USD",
            "CreditCard"
        );

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _jwtToken);

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/payments", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostPayment_SimulatedCardDecline_TransitionsStatusToFailedAndReturns400BadRequest()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        // Payment method ending in 0000 simulates hard decline per SDD 4.7
        var request = new ProcessPaymentRequest(
            orderId,
            89.50m,
            "USD",
            "Card_ending_0000"
        );

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _jwtToken);

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/payments", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Verify state in database transitioned to Failed
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        var dbTxn = await db.Transactions.FirstOrDefaultAsync(t => t.OrderId == orderId);
        dbTxn.Should().NotBeNull();
        dbTxn!.Status.Should().Be(PaymentStatus.Failed);
        dbTxn.FailureReason.Should().Contain("Card Declined");

        // Verify EventBus published failure event
        _factory.MockPublishEndpoint.Verify(
            p => p.Publish(It.Is<PaymentFailedIntegrationEvent>(e => e.OrderId == orderId), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task GetPaymentByOrderId_ExistingPayment_ReturnsCorrectTransactionState()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var request = new ProcessPaymentRequest(
            orderId,
            199.00m,
            "USD",
            "Mastercard_5555"
        );

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _jwtToken);

        var postResponse = await client.PostAsJsonAsync("/api/v1/payments", request);
        postResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act
        var getResponse = await client.GetAsync($"/api/v1/payments/{orderId}");

        // Assert
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var paymentResponse = await getResponse.Content.ReadFromJsonAsync<PaymentResponse>();
        paymentResponse.Should().NotBeNull();
        paymentResponse!.OrderId.Should().Be(orderId);
        paymentResponse.Amount.Should().Be(199.00m);
        paymentResponse.Status.Should().Be("Completed");
        paymentResponse.TransactionId.Should().StartWith("ch_sim_");
    }

    [Fact]
    public async Task GetPaymentByOrderId_NonExistent_Returns404NotFound()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _jwtToken);
        var nonExistentOrderId = Guid.NewGuid();

        // Act
        var response = await client.GetAsync($"/api/v1/payments/{nonExistentOrderId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PostPayment_DuplicatePaymentAttempt_Returns409Conflict()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var request = new ProcessPaymentRequest(
            orderId,
            75.00m,
            "USD",
            "Amex_3782"
        );

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _jwtToken);

        // First attempt - Succeeds
        var firstResponse = await client.PostAsJsonAsync("/api/v1/payments", request);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act: Second duplicate attempt
        var secondResponse = await client.PostAsJsonAsync("/api/v1/payments", request);

        // Assert
        secondResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task OrderCreatedConsumer_ConsumesEvent_CreatesPendingPaymentTransaction()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var orderCreatedEvent = new OrderCreatedIntegrationEvent(
            orderId,
            Guid.NewGuid().ToString(),
            249.99m,
            new List<ShopSphere.Services.Ordering.Application.Events.OrderItemDto>(),
            DateTime.UtcNow
        );

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        var loggerMock = new Mock<ILogger<OrderCreatedConsumer>>();
        var consumer = new OrderCreatedConsumer(db, loggerMock.Object);

        var contextMock = new Mock<ConsumeContext<OrderCreatedIntegrationEvent>>();
        contextMock.Setup(c => c.Message).Returns(orderCreatedEvent);
        contextMock.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        // Act
        await consumer.Consume(contextMock.Object);

        // Assert
        var createdTxn = await db.Transactions.FirstOrDefaultAsync(t => t.OrderId == orderId);
        createdTxn.Should().NotBeNull();
        createdTxn!.OrderId.Should().Be(orderId);
        createdTxn.Amount.Should().Be(249.99m);
        createdTxn.Status.Should().Be(PaymentStatus.Pending);
    }

    [Fact]
    public async Task PostWebhook_StripeSucceededEvent_UpdatesTransactionToCompletedIdempotently()
    {
        // Arrange
        var orderId = Guid.NewGuid();

        // Seed a transaction in Pending status
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
            db.Transactions.Add(new PaymentTransaction
            {
                Id = Guid.NewGuid(),
                OrderId = orderId,
                Amount = 120.00m,
                Currency = "USD",
                Status = PaymentStatus.Pending,
                PaymentMethod = "Stripe",
                CreatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var webhookPayload = new
        {
            type = "payment_intent.succeeded",
            data = new
            {
                @object = new
                {
                    id = "pi_mock_webhook_test_123",
                    metadata = new
                    {
                        orderId = orderId.ToString()
                    }
                }
            }
        };

        var client = _factory.CreateClient();
        var content = new StringContent(
            JsonSerializer.Serialize(webhookPayload),
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await client.PostAsync("/api/v1/payments/webhook", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var webhookResponse = await response.Content.ReadFromJsonAsync<StripeWebhookResponse>();
        webhookResponse.Should().NotBeNull();
        webhookResponse!.Received.Should().BeTrue();

        // Verify DB updated
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
            var txn = await db.Transactions.FirstOrDefaultAsync(t => t.OrderId == orderId);
            txn.Should().NotBeNull();
            txn!.Status.Should().Be(PaymentStatus.Completed);
            txn.TransactionId.Should().Be("pi_mock_webhook_test_123");
        }
    }
}

