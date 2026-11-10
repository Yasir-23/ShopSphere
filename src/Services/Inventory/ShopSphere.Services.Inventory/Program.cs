using Carter;
using FluentValidation;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Serilog;
using ShopSphere.Common.Logging;
using ShopSphere.Common.Logging.HealthChecks;
using ShopSphere.Common.Security;
using ShopSphere.EventBus;
using ShopSphere.Services.Inventory.Consumers;
using ShopSphere.Services.Inventory.Data;
using ShopSphere.Services.Inventory.Services;
using ShopSphere.Services.Inventory.Validators;

var builder = WebApplication.CreateBuilder(args);

// Default to port 5030 for local runs, but honour ASPNETCORE_URLS / --urls (containers bind :8080).
if (string.IsNullOrWhiteSpace(builder.Configuration["urls"]) && string.IsNullOrWhiteSpace(builder.Configuration["ASPNETCORE_URLS"]))
{
    builder.WebHost.UseUrls("http://localhost:5030");
}

// 1. Serilog Structured Logging & OpenTelemetry Distributed Tracing
builder.Services.AddShopSphereLogging(builder.Configuration, "ShopSphere.Services.Inventory");

// 2. PostgreSQL Persistence Layer via EF Core
var connectionString = builder.Configuration.GetConnectionString("InventoryDb")
    ?? "Host=localhost;Port=15432;Database=shopsphere_inventory;Username=postgres;Password=ShopSphere_Postgres_2026!;";

builder.Services.AddDbContext<InventoryDbContext>(options =>
{
    options.UseNpgsql(connectionString);
});

// 3. Application Domain Services
builder.Services.AddScoped<IInventoryService, InventoryService>();

// 3b. MassTransit / RabbitMQ: checkout saga participants (reserve, release, commit stock)
builder.Services.AddShopSphereEventBus(builder.Configuration, bus =>
{
    bus.SetKebabCaseEndpointNameFormatter();
    bus.AddConsumer<ReserveInventoryConsumer>();
    bus.AddConsumer<ReleaseInventoryConsumer>();
    bus.AddConsumer<CommitInventoryConsumer>();
});

// 4. JWT Authentication & Role Authorization Security
builder.Services.AddShopSphereSecurity(builder.Configuration);

// 5. FluentValidation & Carter Minimal API Modules
builder.Services.AddValidatorsFromAssemblyContaining<ReserveStockRequestValidator>();
builder.Services.AddCarter();

// Health Checks
builder.Services.AddHealthChecks()
    .AddShopSphereDbContextCheck<InventoryDbContext>("inventory-db");

// 6. Swagger / OpenAPI Documentation with JWT Bearer Security Scheme
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "ShopSphere Inventory API",
        Version = "v1",
        Description = "Stock Ledger, Concurrency Control, and Atomic Stock Allocations (PostgreSQL + EF Core)"
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

// 7. Serilog HTTP Request Logging (registered immediately after builder.Build())
app.UseSerilogRequestLogging();

// 8. Automatic Database Migration & Seeding
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

    try
    {
        if (dbContext.Database.IsRelational())
        {
            logger.LogInformation("Applying relational database migrations for InventoryDbContext...");
            await dbContext.Database.MigrateAsync();
            logger.LogInformation("Database migrations applied successfully.");
        }
        else
        {
            logger.LogInformation("Ensuring database created for provider...");
            await dbContext.Database.EnsureCreatedAsync();
        }

        await InventoryDbSeeder.SeedAsync(dbContext, logger);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred during database migration or seeding.");
        if (app.Environment.IsEnvironment("Testing"))
        {
            throw;
        }
    }
}

// 9. HTTP Pipeline Middleware
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "ShopSphere Inventory API v1");
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

