using Carter;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using ShopSphere.Common.Logging;
using ShopSphere.Common.Logging.HealthChecks;
using ShopSphere.Common.Security;
using ShopSphere.Services.Identity.Data;
using ShopSphere.Services.Identity.Data.Entities;
using ShopSphere.Services.Identity.Models;
using ShopSphere.Services.Identity.Services;

var builder = WebApplication.CreateBuilder(args);

// 1. Serilog & OpenTelemetry Structured Observability
builder.Services.AddShopSphereLogging(builder.Configuration, "ShopSphere.Services.Identity");

// 2. Database Persistence Layer
var connectionString = builder.Configuration.GetConnectionString("IdentityDb")
    ?? throw new InvalidOperationException("Connection string 'IdentityDb' not configured.");

builder.Services.AddDbContext<IdentityDbContext>(options =>
{
    options.UseSqlServer(connectionString);
});

// 3. ASP.NET Core Identity Core Configuration
builder.Services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequiredLength = 8;
    options.User.RequireUniqueEmail = true;
})
.AddEntityFrameworkStores<IdentityDbContext>()
.AddDefaultTokenProviders();

// 4. JWT Authentication & Authorization Security
builder.Services.AddShopSphereSecurity(builder.Configuration);

// 5. Custom Domain Services
builder.Services.AddScoped<ITokenService, TokenService>();

// 6. FluentValidation & Carter Minimal API Modules
builder.Services.AddValidatorsFromAssemblyContaining<RegisterRequestValidator>();
builder.Services.AddCarter();

// Health Checks
builder.Services.AddHealthChecks()
    .AddShopSphereDbContextCheck<IdentityDbContext>("identity-db");

// 7. Swagger/OpenAPI Documentation with JWT Bearer Security Scheme
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "ShopSphere Identity API",
        Version = "v1",
        Description = "Authentication, Token Management, and Role Authorization for ShopSphere"
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Enter your token below.",
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

// 8. Automatic Database Migration & Seeding
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
    
    try
    {
        logger.LogInformation("Verifying and applying database migrations for IdentityDbContext...");
        await dbContext.Database.MigrateAsync();
        logger.LogInformation("Database migrations applied successfully.");

        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        await IdentityDbSeeder.SeedRolesAsync(roleManager, logger);
        logger.LogInformation("Database roles seeded successfully.");

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        await IdentityDbSeeder.SeedDefaultAdminUserAsync(userManager, app.Logger);
        logger.LogInformation("Default administrator user seeded successfully.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred during database migration or seeding.");
        throw;
    }
}

// 9. HTTP Pipeline Middleware
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "ShopSphere Identity API v1");
    c.RoutePrefix = "swagger";
});

app.UseAuthentication();
app.UseAuthorization();

// 10. Map Carter Endpoints
app.MapCarter();
ShopSphere.Common.Logging.HealthChecks.HealthCheckExtensions.MapShopSphereHealthChecks(app);

app.Run();
