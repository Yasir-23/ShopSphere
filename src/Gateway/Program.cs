using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Serilog;
using ShopSphere.Common.Logging;

var builder = WebApplication.CreateBuilder(args);

// Explicitly bind Kestrel to Port 5001 in non-testing environments
if (!builder.Environment.IsEnvironment("Testing"))
{
    var configuredUrls = builder.Configuration["ASPNETCORE_URLS"];
    if (string.IsNullOrWhiteSpace(configuredUrls))
    {
        builder.WebHost.UseUrls("http://localhost:5001");
    }
}

// Ephemeral Data Protection for stateless Gateway
builder.Services.AddDataProtection()
    .UseEphemeralDataProtectionProvider();

// 1. Serilog Structured Logging & OpenTelemetry Distributed Tracing
builder.Services.AddShopSphereLogging(builder.Configuration, "ShopSphere.ApiGateway");

// 2. Centralized CORS Policy
const string corsPolicyName = "CorsPolicy";
builder.Services.AddCors(options =>
{
    options.AddPolicy(corsPolicyName, policy =>
    {
        policy.WithOrigins("http://localhost:3000", "http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });

    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:3000", "http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// 3. Centralized IP-based Fixed Window Rate Limiting
builder.Services.AddRateLimiter(rateLimiterOptions =>
{
    rateLimiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    static string ResolveClientIp(HttpContext context)
    {
        var forwarded = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            return forwarded.Split(',')[0].Trim();
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
    }

    // Register primary policy and alias for route configuration flexibility
    rateLimiterOptions.AddPolicy("fixed-window-ip", httpContext =>
    {
        var configuration = httpContext.RequestServices.GetRequiredService<IConfiguration>();
        var limit = configuration.GetValue<int?>("RateLimiting:PermitLimit") ?? 100;
        var window = configuration.GetValue<int?>("RateLimiting:WindowSeconds") ?? 60;
        var queue = configuration.GetValue<int?>("RateLimiting:QueueLimit") ?? 0;

        var ip = ResolveClientIp(httpContext);
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ip,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limit,
                Window = TimeSpan.FromSeconds(window),
                QueueLimit = queue,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            });
    });

    rateLimiterOptions.AddPolicy("ip-fixed-window", httpContext =>
    {
        var configuration = httpContext.RequestServices.GetRequiredService<IConfiguration>();
        var limit = configuration.GetValue<int?>("RateLimiting:PermitLimit") ?? 100;
        var window = configuration.GetValue<int?>("RateLimiting:WindowSeconds") ?? 60;
        var queue = configuration.GetValue<int?>("RateLimiting:QueueLimit") ?? 0;

        var ip = ResolveClientIp(httpContext);
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ip,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limit,
                Window = TimeSpan.FromSeconds(window),
                QueueLimit = queue,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            });
    });
});

// 4. Health Checks
builder.Services.AddHealthChecks();

// 5. YARP Reverse Proxy Configuration
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

// 6. Serilog HTTP Request Logging (registered immediately after builder.Build())
app.UseSerilogRequestLogging();

// 7. Cross-Cutting Middleware Pipeline
app.UseCors(corsPolicyName);

app.UseRateLimiter();

// 8. Health & Diagnostic Endpoints
app.MapHealthChecks("/health");
ShopSphere.Common.Logging.HealthChecks.HealthCheckExtensions.MapShopSphereHealthChecks(app);
app.MapGet("/ping", () => Results.Ok(new { status = "Healthy", timestamp = DateTime.UtcNow }));
app.MapGet("/test/ratelimit", () => Results.Ok(new { status = "Ok" }))
   .RequireRateLimiting("fixed-window-ip");
app.MapGet("/test/echo-auth", (HttpContext context) =>
    Results.Ok(new { auth = context.Request.Headers.Authorization.ToString() }));

// 9. YARP Reverse Proxy Endpoints
app.MapReverseProxy();

app.Run();

public partial class Program { }
