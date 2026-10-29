using Carter;
using FluentValidation;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.OpenApi.Models;
using Serilog;
using ShopSphere.Common.Logging;
using ShopSphere.Common.Security;
using ShopSphere.Services.Catalog.Api.Configuration;
using ShopSphere.Services.Catalog.Api.Data;
using ShopSphere.Services.Catalog.Api.Grpc;
using ShopSphere.Services.Catalog.Api.Validators;

AppContext.SetSwitch("Microsoft.AspNetCore.Server.Kestrel.EnableAlternateSchemes", true);

var builder = WebApplication.CreateBuilder(args);

// 1. Structured Logging & Distributed Tracing
builder.Services.AddShopSphereLogging(builder.Configuration, "ShopSphere.Services.Catalog");

// 2. Kestrel Protocols Configuration: Dedicated REST (HTTP/1.1) and gRPC (HTTP/2 cleartext) endpoints
builder.WebHost.ConfigureKestrel(options =>
{
    // Port 5010: REST APIs and Swagger UI (HTTP/1.1)
    options.ListenLocalhost(5010, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http1;
    });

    // Port 5011: High-Performance gRPC Price Checking Service (HTTP/2 cleartext)
    options.ListenLocalhost(5011, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http2;
    });
});

// 3. MongoDB Persistence Configuration
builder.Services.Configure<MongoDbOptions>(builder.Configuration.GetSection(MongoDbOptions.SectionName));
builder.Services.AddSingleton<ICatalogDbContext, CatalogDbContext>();

// 4. JWT Authentication & Role Authorization
builder.Services.AddShopSphereSecurity(builder.Configuration);

// 5. gRPC Engine
builder.Services.AddGrpc();

// 6. FluentValidation & Carter Minimal API Modules
builder.Services.AddValidatorsFromAssemblyContaining<CreateProductRequestValidator>();
builder.Services.AddCarter();

// 7. Swagger / OpenAPI Documentation with JWT Bearer Security Scheme
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "ShopSphere Catalog API",
        Version = "v1",
        Description = "Product Discovery, Governance, and High-Performance Price Checking (REST + gRPC + MongoDB)"
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

// Enable HTTP Request Logging to Seq via Serilog
app.UseSerilogRequestLogging();

// 8. Automatic MongoDB Index Creation & Initial Seeding
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var dbContext = scope.ServiceProvider.GetRequiredService<ICatalogDbContext>();

    try
    {
        logger.LogInformation("Initializing and verifying Catalog MongoDB collections and indices...");
        await CatalogDbSeeder.SeedAsync(dbContext, logger);
        logger.LogInformation("Catalog MongoDB setup and seeding completed successfully.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred during Catalog MongoDB initialization or seeding.");
    }
}

// 9. HTTP Pipeline Middleware
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "ShopSphere Catalog API v1");
    c.RoutePrefix = "swagger";
});

app.UseAuthentication();
app.UseAuthorization();

// 10. Map Carter Minimal API Endpoints & gRPC Service
app.MapCarter();
app.MapGrpcService<CatalogPriceCheckerService>();

app.MapGet("/", () => Results.Redirect("/swagger"));

app.Run();
