using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Yarp.ReverseProxy.Configuration;
using ShopSphere.Tests.Common;

namespace ShopSphere.ApiGateway.Tests;

public class GatewayWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            services.AddTestResponseBodyFeatureWorkaround();
        });
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:PermitLimit"] = "3",
                ["RateLimiting:WindowSeconds"] = "60",
                ["RateLimiting:QueueLimit"] = "0"
            });
        });
    }
}

public class GatewayIntegrationTests : IClassFixture<GatewayWebApplicationFactory>
{
    private readonly GatewayWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public GatewayIntegrationTests(GatewayWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Test1_GatewayBootsAndReturnsHealthyOnHealthAndPingEndpoints()
    {
        // Act - Health endpoint
        var healthResponse = await _client.GetAsync("/health");

        // Assert - Health
        healthResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var healthContent = await healthResponse.Content.ReadAsStringAsync();
        healthContent.Should().Be("Healthy");

        // Act - Ping endpoint
        var pingResponse = await _client.GetAsync("/ping");

        // Assert - Ping
        pingResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var pingContent = await pingResponse.Content.ReadAsStringAsync();
        pingContent.Should().Contain("Healthy");
    }

    [Fact]
    public void Test2_YarpRouteConfigurationValidation_VerifiesAllDownstreamRoutesAndClusters()
    {
        // Arrange
        var proxyConfigProvider = _factory.Services.GetRequiredService<IProxyConfigProvider>();
        var proxyConfig = proxyConfigProvider.GetConfig();

        // Assert - Routes Count
        proxyConfig.Routes.Should().HaveCount(6);

        // Verify all 6 expected routes
        var expectedRoutes = new Dictionary<string, (string Path, string ClusterId)>
        {
            ["identity-route"] = ("/api/v1/identity/{**catch-all}", "identity-cluster"),
            ["catalog-route"] = ("/api/v1/catalog/{**catch-all}", "catalog-cluster"),
            ["basket-route"] = ("/api/v1/basket/{**catch-all}", "basket-cluster"),
            ["inventory-route"] = ("/api/v1/inventory/{**catch-all}", "inventory-cluster"),
            ["ordering-route"] = ("/api/v1/orders/{**catch-all}", "ordering-cluster"),
            ["payment-route"] = ("/api/v1/payments/{**catch-all}", "payment-cluster")
        };

        foreach (var (routeId, (expectedPath, expectedCluster)) in expectedRoutes)
        {
            var route = proxyConfig.Routes.FirstOrDefault(r => r.RouteId == routeId);
            route.Should().NotBeNull($"Route '{routeId}' must be configured");
            route!.ClusterId.Should().Be(expectedCluster);
            route.Match.Path.Should().Be(expectedPath);
            route.RateLimiterPolicy.Should().Be("fixed-window-ip");
            route.CorsPolicy.Should().Be("CorsPolicy");
        }

        // Assert - Clusters Count
        proxyConfig.Clusters.Should().HaveCount(6);

        // Verify all 6 expected clusters and downstream endpoints
        var expectedClusters = new Dictionary<string, string>
        {
            ["identity-cluster"] = "http://localhost:5005",
            ["catalog-cluster"] = "http://localhost:5010",
            ["basket-cluster"] = "http://localhost:5020",
            ["inventory-cluster"] = "http://localhost:5030",
            ["ordering-cluster"] = "http://localhost:5040",
            ["payment-cluster"] = "http://localhost:5050"
        };

        foreach (var (clusterId, expectedAddress) in expectedClusters)
        {
            var cluster = proxyConfig.Clusters.FirstOrDefault(c => c.ClusterId == clusterId);
            cluster.Should().NotBeNull($"Cluster '{clusterId}' must be configured");
            cluster!.Destinations.Should().NotBeNull();
            cluster.Destinations!.Values.Select(d => d.Address)
                .Should().Contain(expectedAddress, $"Cluster '{clusterId}' must target '{expectedAddress}'");

            // Verify active health check configuration
            cluster.HealthCheck.Should().NotBeNull();
            cluster.HealthCheck!.Active.Should().NotBeNull();
            cluster.HealthCheck.Active!.Enabled.Should().BeTrue();
            cluster.HealthCheck.Active.Path.Should().Be("/health");
            cluster.HealthCheck.Active.Interval.Should().Be(TimeSpan.FromSeconds(30));
            cluster.HealthCheck.Active.Timeout.Should().Be(TimeSpan.FromSeconds(5));

            // Verify HTTP request activity timeout configuration
            cluster.HttpRequest.Should().NotBeNull();
            cluster.HttpRequest!.ActivityTimeout.Should().Be(TimeSpan.FromSeconds(30));
        }
    }

    [Fact]
    public async Task Test3_RateLimiterPolicy_BlocksExcessiveTrafficBeyondThresholdWith429()
    {
        // The factory in this test fixture sets PermitLimit to 3.
        // Send 3 allowed requests, then verify the 4th request yields 429 Too Many Requests.
        var uniqueIp = "192.168.1." + Random.Shared.Next(10, 250);

        for (var i = 0; i < 3; i++)
        {
            using var allowedRequest = new HttpRequestMessage(HttpMethod.Get, "/test/ratelimit");
            allowedRequest.Headers.Add("X-Forwarded-For", uniqueIp);

            var allowedResponse = await _client.SendAsync(allowedRequest);
            allowedResponse.StatusCode.Should().Be(HttpStatusCode.OK, $"Request {i + 1} should be within the permit limit");
        }

        // 4th request must be rate limited
        using var rateLimitedRequest = new HttpRequestMessage(HttpMethod.Get, "/test/ratelimit");
        rateLimitedRequest.Headers.Add("X-Forwarded-For", uniqueIp);

        var blockedResponse = await _client.SendAsync(rateLimitedRequest);
        blockedResponse.StatusCode.Should().Be(HttpStatusCode.TooManyRequests, "4th request should exceed the permit limit of 3");
    }

    [Theory]
    [InlineData("http://localhost:3000")]
    [InlineData("http://localhost:5173")]
    public async Task Test4_CorsHeaders_ArePresentOnPreflightOptionsRequests(string origin)
    {
        // Arrange preflight request
        using var preflightRequest = new HttpRequestMessage(HttpMethod.Options, "/api/v1/catalog/products");
        preflightRequest.Headers.Add("Origin", origin);
        preflightRequest.Headers.Add("Access-Control-Request-Method", "GET");
        preflightRequest.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");

        // Act
        var response = await _client.SendAsync(preflightRequest);

        // Assert - preflight success
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

        // Verify CORS response headers
        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeTrue();
        response.Headers.GetValues("Access-Control-Allow-Origin").Should().Contain(origin);

        response.Headers.Contains("Access-Control-Allow-Credentials").Should().BeTrue();
        response.Headers.GetValues("Access-Control-Allow-Credentials").Should().Contain("true");

        response.Headers.Contains("Access-Control-Allow-Methods").Should().BeTrue();
        var allowedMethods = string.Join(",", response.Headers.GetValues("Access-Control-Allow-Methods"));
        allowedMethods.Should().Contain("GET");

        response.Headers.Contains("Access-Control-Allow-Headers").Should().BeTrue();
    }

    [Fact]
    public async Task Test5_AuthorizationHeader_IsPreservedAndForwardedTransparently()
    {
        // Arrange
        const string testToken = "Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.e30.test";
        using var request = new HttpRequestMessage(HttpMethod.Get, "/test/echo-auth");
        request.Headers.Add("Authorization", testToken);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain(testToken);
    }
}
