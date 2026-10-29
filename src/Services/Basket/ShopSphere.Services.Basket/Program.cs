using Carter;
using FluentValidation;
using Microsoft.OpenApi.Models;
using Serilog;
using ShopSphere.Common.Logging;
using ShopSphere.Common.Resilience;
using ShopSphere.Common.Security;
using ShopSphere.Services.Basket.Configuration;
using ShopSphere.Services.Basket.Repositories;
using ShopSphere.Services.Basket.Services;
using ShopSphere.Services.Basket.Validators;
using ShopSphere.Services.Catalog.Grpc;
using StackExchange.Redis;

// Enable cleartext HTTP/2 for gRPC client without TLS in development
AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

var builder = WebApplication.CreateBuilder(args);

// Explicitly bind Kestrel to Port 5020 for Basket Service
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(5020);
});

// 1. Serilog Structured Logging & OpenTelemetry Distributed Tracing
builder.Services.AddShopSphereLogging(builder.Configuration, "ShopSphere.Services.Basket");

// 2. Redis Persistence Layer
var redisSection = builder.Configuration.GetSection(RedisOptions.SectionName);
builder.Services.Configure<RedisOptions>(redisSection);
var redisOptions = redisSection.Get<RedisOptions>() ?? new RedisOptions();

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var configurationOptions = ConfigurationOptions.Parse(redisOptions.ConnectionString);
    configurationOptions.AbortOnConnectFail = false;
    return ConnectionMultiplexer.Connect(configurationOptions);
});
builder.Services.AddScoped<IBasketRepository, RedisBasketRepository>();

// 3. gRPC Client Configuration with Standard Polly Resilience Pipeline
var catalogGrpcUrl = builder.Configuration["Services:Catalog:GrpcUrl"]
    ?? builder.Configuration["CatalogService:GrpcUrl"]
    ?? "http://localhost:5011";

builder.Services.AddGrpcClient<CatalogPriceChecker.CatalogPriceCheckerClient>(options =>
{
    options.Address = new Uri(catalogGrpcUrl);
})
.AddShopSphereResilienceHandler();

builder.Services.AddScoped<IPriceValidationService, PriceValidationService>();

// 4. JWT Authentication & Role Authorization
builder.Services.AddShopSphereSecurity(builder.Configuration);

// 5. FluentValidation & Carter Minimal API Modules
builder.Services.AddValidatorsFromAssemblyContaining<UpdateBasketRequestValidator>();
builder.Services.AddCarter();

// 6. Swagger / OpenAPI Documentation with JWT Bearer Security Scheme
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "ShopSphere Basket API",
        Version = "v1",
        Description = "Customer Shopping Basket & Ephemeral Cart Management (Redis + gRPC Price Validation)"
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

// 7. Serilog HTTP Request Logging
app.UseSerilogRequestLogging();

// 8. HTTP Pipeline Middleware
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "ShopSphere Basket API v1");
    c.RoutePrefix = "swagger";
});

app.UseAuthentication();
app.UseAuthorization();

// 9. Map Carter Minimal API Endpoints
app.MapCarter();
app.MapGet("/", () => Results.Redirect("/swagger"));

app.Run();

public partial class Program { }
