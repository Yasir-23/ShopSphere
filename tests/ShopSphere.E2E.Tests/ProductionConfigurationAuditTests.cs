using System.IO;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ShopSphere.Common.Logging.HealthChecks;
using Xunit;

namespace ShopSphere.E2E.Tests;

public class ProductionConfigurationAuditTests
{
    private static readonly string SolutionRoot = FindSolutionRoot();

    private static string FindSolutionRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current != null && !File.Exists(Path.Combine(current.FullName, "ShopSphere.sln")))
        {
            current = current.Parent;
        }

        return current?.FullName ?? Directory.GetCurrentDirectory();
    }

    [Fact]
    public void ProductionDockerCompose_ContainsAllRequiredServices_AndValidConfigurations()
    {
        // Arrange
        var composePath = Path.Combine(SolutionRoot, "docker", "docker-compose.prod.yml");
        File.Exists(composePath).Should().BeTrue("docker-compose.prod.yml must exist under docker/");

        var composeContent = File.ReadAllText(composePath);

        // Required microservices + API Gateway
        var expectedAppServices = new[]
        {
            "identity-service:",
            "catalog-service:",
            "basket-service:",
            "inventory-service:",
            "ordering-service:",
            "payment-service:",
            "api-gateway:"
        };

        foreach (var svc in expectedAppServices)
        {
            composeContent.Should().Contain(svc, $"Production compose must declare {svc}");
        }

        // Required backing infrastructure
        var expectedInfraServices = new[]
        {
            "sqlserver:",
            "postgres:",
            "mongodb:",
            "redis:",
            "rabbitmq:",
            "seq:",
            "jaeger:"
        };

        foreach (var infra in expectedInfraServices)
        {
            composeContent.Should().Contain(infra, $"Production compose must declare {infra}");
        }

        // Assert network definition
        composeContent.Should().Contain("shopsphere-network:");

        // Assert database / broker bindings
        composeContent.Should().Contain("ConnectionStrings__IdentityDb");
        composeContent.Should().Contain("MongoDb__ConnectionString");
        composeContent.Should().Contain("Redis__ConnectionString");
        composeContent.Should().Contain("ConnectionStrings__InventoryDb");
        composeContent.Should().Contain("ConnectionStrings__OrderingDb");
        composeContent.Should().Contain("RabbitMQ__Host");
        composeContent.Should().Contain("ConnectionStrings__PaymentDb");

        // Assert healthcheck definitions and dependencies
        composeContent.Should().Contain("/health/live");
        composeContent.Should().Contain("condition: service_healthy");
    }

    [Fact]
    public void MultiStageDockerfiles_ConfiguredWithNonRootUser_AndReleaseBuild()
    {
        var dockerfiles = new[]
        {
            Path.Combine(SolutionRoot, "src", "Gateway", "Dockerfile"),
            Path.Combine(SolutionRoot, "src", "Services", "Identity", "ShopSphere.Services.Identity", "Dockerfile"),
            Path.Combine(SolutionRoot, "src", "Services", "Catalog", "ShopSphere.Services.Catalog.Api", "Dockerfile"),
            Path.Combine(SolutionRoot, "src", "Services", "Basket", "ShopSphere.Services.Basket", "Dockerfile"),
            Path.Combine(SolutionRoot, "src", "Services", "Inventory", "ShopSphere.Services.Inventory", "Dockerfile"),
            Path.Combine(SolutionRoot, "src", "Services", "Ordering", "ShopSphere.Services.Ordering.Api", "Dockerfile"),
            Path.Combine(SolutionRoot, "src", "Services", "Payment", "Dockerfile")
        };

        foreach (var dfPath in dockerfiles)
        {
            File.Exists(dfPath).Should().BeTrue($"Dockerfile at '{dfPath}' must exist.");
            var content = File.ReadAllText(dfPath);

            content.Should().Contain("mcr.microsoft.com/dotnet/aspnet:8.0-alpine", $"Dockerfile at '{dfPath}' must use net8.0 alpine base");
            content.Should().Contain("mcr.microsoft.com/dotnet/sdk:8.0-alpine", $"Dockerfile at '{dfPath}' must use net8.0 alpine sdk");
            content.Should().Contain("-c Release --no-restore", $"Dockerfile at '{dfPath}' must build with -c Release --no-restore");
            content.Should().Contain("USER app", $"Dockerfile at '{dfPath}' must run as non-root 'USER app'");
        }

        var dockerignorePath = Path.Combine(SolutionRoot, ".dockerignore");
        File.Exists(dockerignorePath).Should().BeTrue(".dockerignore must exist at repository root.");
        var ignoreContent = File.ReadAllText(dockerignorePath);
        ignoreContent.Should().Contain("bin");
        ignoreContent.Should().Contain("obj");
        ignoreContent.Should().Contain(".git");
        ignoreContent.Should().Contain(".vs");
    }

    [Fact]
    public void AllServices_ConfigureStandardHealthCheckEndpoints()
    {
        var servicePrograms = new[]
        {
            Path.Combine(SolutionRoot, "src", "Gateway", "Program.cs"),
            Path.Combine(SolutionRoot, "src", "Services", "Identity", "ShopSphere.Services.Identity", "Program.cs"),
            Path.Combine(SolutionRoot, "src", "Services", "Catalog", "ShopSphere.Services.Catalog.Api", "Program.cs"),
            Path.Combine(SolutionRoot, "src", "Services", "Basket", "ShopSphere.Services.Basket", "Program.cs"),
            Path.Combine(SolutionRoot, "src", "Services", "Inventory", "ShopSphere.Services.Inventory", "Program.cs"),
            Path.Combine(SolutionRoot, "src", "Services", "Ordering", "ShopSphere.Services.Ordering.Api", "Program.cs"),
            Path.Combine(SolutionRoot, "src", "Services", "Payment", "Program.cs")
        };

        foreach (var prog in servicePrograms)
        {
            File.Exists(prog).Should().BeTrue($"Program.cs at '{prog}' must exist.");
            var content = File.ReadAllText(prog);
            content.Should().Contain("MapShopSphereHealthChecks", $"Program.cs at '{prog}' must map standard ShopSphere health checks.");
        }
    }

    [Fact]
    public async Task HealthCheckResponseWriter_FormatsHealthyResponseAsJson()
    {
        // Arrange
        var context = new DefaultHttpContext();
        var memoryStream = new MemoryStream();
        context.Response.Body = memoryStream;

        var entries = new Dictionary<string, HealthReportEntry>
        {
            ["database"] = new HealthReportEntry(
                HealthStatus.Healthy,
                "Database reachable",
                TimeSpan.FromMilliseconds(5),
                null,
                null)
        };

        var report = new HealthReport(entries, TimeSpan.FromMilliseconds(10));

        // Act
        await HealthCheckResponseWriter.WriteResponse(context, report);

        // Assert
        context.Response.ContentType.Should().Be("application/json");
        memoryStream.Seek(0, SeekOrigin.Begin);
        using var doc = await JsonDocument.ParseAsync(memoryStream);
        var root = doc.RootElement;

        root.GetProperty("status").GetString().Should().Be("Healthy");
        root.GetProperty("totalDuration").GetString().Should().NotBeNullOrWhiteSpace();
        root.GetProperty("entries").GetProperty("database").GetProperty("status").GetString().Should().Be("Healthy");
    }
}

