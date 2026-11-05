using Carter;
using FluentValidation;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Serilog;
using ShopSphere.Common.Logging;
using ShopSphere.Common.Security;
using ShopSphere.Services.Payment.Consumers;
using ShopSphere.Services.Payment.Data;
using ShopSphere.Services.Payment.Services;
using ShopSphere.Services.Payment.Validators;

var builder = WebApplication.CreateBuilder(args);

// Explicitly bind Kestrel to Port 5050 for Payment Service
builder.WebHost.UseUrls("http://localhost:5050");

// 1. Serilog Structured Logging & OpenTelemetry Distributed Tracing
builder.Services.AddShopSphereLogging(builder.Configuration, "ShopSphere.Services.Payment");

// 2. PostgreSQL Persistence Layer via EF Core
var connectionString = builder.Configuration.GetConnectionString("PaymentDb")
    ?? "Host=localhost;Port=15432;Database=shopsphere_payment;Username=postgres;Password=ShopSphere_Postgres_2026!;";

builder.Services.AddDbContext<PaymentDbContext>(options =>
{
    options.UseNpgsql(connectionString);
});

// 3. MassTransit with RabbitMQ
builder.Services.AddMassTransit(busConfigurator =>
{
    busConfigurator.SetKebabCaseEndpointNameFormatter();
    busConfigurator.AddConsumer<OrderCreatedConsumer>();

    busConfigurator.UsingRabbitMq((context, cfg) =>
    {
        var host = builder.Configuration["RabbitMQ:Host"]
            ?? Environment.GetEnvironmentVariable("RABBITMQ_HOST")
            ?? "localhost";
        var portString = builder.Configuration["RabbitMQ:Port"]
            ?? Environment.GetEnvironmentVariable("RABBITMQ_PORT")
            ?? "15672";
        var username = builder.Configuration["RabbitMQ:Username"]
            ?? Environment.GetEnvironmentVariable("RABBITMQ_USERNAME")
            ?? Environment.GetEnvironmentVariable("RABBITMQ_DEFAULT_USER")
            ?? "shopsphere";
        var password = builder.Configuration["RabbitMQ:Password"]
            ?? Environment.GetEnvironmentVariable("RABBITMQ_PASSWORD")
            ?? Environment.GetEnvironmentVariable("RABBITMQ_DEFAULT_PASS")
            ?? "ShopSphere_RabbitMQ_2026!";

        ushort port = 5672;
        if (ushort.TryParse(portString, out var parsedPort))
        {
            port = parsedPort;
        }

        cfg.Host(host, port, "/", h =>
        {
            h.Username(username);
            h.Password(password);
        });

        cfg.UseMessageRetry(r => r.Incremental(3, TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(500)));
        cfg.ConfigureEndpoints(context);
    });
});

// 4. Domain & Gateway Payment Services
builder.Services.AddScoped<IPaymentProcessor, StripePaymentProcessor>();
builder.Services.AddScoped<IPaymentService, PaymentService>();

// 5. JWT Authentication & Role Authorization Security
builder.Services.AddShopSphereSecurity(builder.Configuration);

// 6. FluentValidation & Carter Minimal API Modules
builder.Services.AddValidatorsFromAssemblyContaining<ProcessPaymentRequestValidator>();
builder.Services.AddCarter();

// 7. Swagger / OpenAPI Documentation with JWT Bearer Security Scheme
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "ShopSphere Payment API",
        Version = "v1",
        Description = "Payment Processing & Reconciliation Service (Stripe Integration / Mock Provider + PostgreSQL + MassTransit RabbitMQ)"
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

// 8. Serilog HTTP Request Logging (registered immediately after builder.Build())
app.UseSerilogRequestLogging();

// 9. Automatic Database Migration on Startup
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var dbContext = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();

    try
    {
        if (dbContext.Database.IsRelational())
        {
            logger.LogInformation("Applying relational database migrations for PaymentDbContext...");
            await dbContext.Database.MigrateAsync();
            logger.LogInformation("Payment database migrations applied successfully.");
        }
        else
        {
            logger.LogInformation("Ensuring database created for provider...");
            await dbContext.Database.EnsureCreatedAsync();
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred during Payment database migration.");
        if (app.Environment.IsEnvironment("Testing"))
        {
            throw;
        }
    }
}

// 10. HTTP Pipeline Middleware
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "ShopSphere Payment API v1");
    c.RoutePrefix = "swagger";
});

app.UseAuthentication();
app.UseAuthorization();

// 11. Map Carter Minimal API Endpoints
app.MapCarter();
app.MapGet("/", () => Results.Redirect("/swagger"));

app.Run();

public partial class Program { }

