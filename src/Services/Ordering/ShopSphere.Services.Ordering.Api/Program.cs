using Carter;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Serilog;
using ShopSphere.Common.Logging;
using ShopSphere.Common.Logging.HealthChecks;
using ShopSphere.Common.Security;
using ShopSphere.Services.Ordering.Application;
using ShopSphere.Services.Ordering.Infrastructure;
using ShopSphere.Services.Ordering.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Default to port 5040 for local runs, but honour ASPNETCORE_URLS / --urls (containers bind :8080).
if (string.IsNullOrWhiteSpace(builder.Configuration["urls"]) && string.IsNullOrWhiteSpace(builder.Configuration["ASPNETCORE_URLS"]))
{
    builder.WebHost.UseUrls("http://localhost:5040");
}

// 1. Serilog Structured Logging & OpenTelemetry Distributed Tracing
builder.Services.AddShopSphereLogging(builder.Configuration, "ShopSphere.Services.Ordering");

// 2. JWT Authentication & Role Authorization
builder.Services.AddShopSphereSecurity(builder.Configuration);

// 3. Application Layer (MediatR CQRS + FluentValidation)
builder.Services.AddOrderingApplication();

// 4. Infrastructure Layer (EF Core SQL Server + MassTransit RabbitMQ)
builder.Services.AddOrderingInfrastructure(builder.Configuration);

// 5. Carter Minimal API Modules
builder.Services.AddCarter();

// Health Checks
builder.Services.AddHealthChecks()
    .AddShopSphereDbContextCheck<OrderingDbContext>("ordering-db");

// 6. Swagger / OpenAPI Documentation with JWT Bearer Security Scheme
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "ShopSphere Ordering API",
        Version = "v1",
        Description = "Order Lifecycle Management & CQRS Service (EF Core SQL Server + MassTransit RabbitMQ)"
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Enter your token below without 'Bearer ' prefix.",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// 7. Serilog HTTP Request Logging enabled right after builder.Build()
app.UseSerilogRequestLogging();

// 8. Automatic Database Migrations on Startup
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var dbContext = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();

    if (dbContext.Database.IsRelational())
    {
        try
        {
            logger.LogInformation("Verifying and applying database migrations for OrderingDbContext...");
            await dbContext.Database.MigrateAsync();
            logger.LogInformation("Ordering database migrations applied successfully.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred during Ordering database migration.");
            throw;
        }
    }
}

// 9. HTTP Pipeline Middleware
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "ShopSphere Ordering API v1");
    c.RoutePrefix = "swagger";
});

app.UseAuthentication();
app.UseAuthorization();

// 10. Map Carter Minimal API Endpoints
app.MapCarter();
ShopSphere.Common.Logging.HealthChecks.HealthCheckExtensions.MapShopSphereHealthChecks(app);
app.MapGet("/", () => Results.Redirect("/swagger"));

app.Run();

public partial class Program { }

