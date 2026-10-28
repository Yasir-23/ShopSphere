using Carter;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using ShopSphere.Common.Security;
using ShopSphere.Services.Identity.Data.Entities;
using ShopSphere.Services.Identity.Models;
using ShopSphere.Services.Identity.Services;

namespace ShopSphere.Services.Identity.Endpoints;

public class IdentityEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/identity")
            .WithTags("Identity");

        group.MapPost("/register", RegisterUser)
            .WithName("RegisterUser")
            .Produces<RegisterResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/login", LoginUser)
            .WithName("LoginUser")
            .Produces<LoginResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/refresh", RefreshToken)
            .WithName("RefreshToken")
            .Produces<LoginResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/revoke", RevokeToken)
            .WithName("RevokeToken")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/me", GetCurrentUser)
            .WithName("GetCurrentUser")
            .RequireAuthorization()
            .Produces<UserProfileResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);
    }

    private static async Task<IResult> RegisterUser(
        RegisterRequest request,
        IValidator<RegisterRequest> validator,
        UserManager<ApplicationUser> userManager,
        HttpContext httpContext)
    {
        var validationResult = await validator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return Results.ValidationProblem(validationResult.ToDictionary(), 
                detail: "One or more validation errors occurred.", 
                instance: httpContext.Request.Path);
        }

        var existingUser = await userManager.FindByEmailAsync(request.Email);
        if (existingUser != null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Conflict",
                detail: "Email address is already registered.",
                instance: httpContext.Request.Path,
                type: "https://shopsphere.local/errors/conflict");
        }

        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName,
            CreatedAtUtc = DateTime.UtcNow
        };

        var createResult = await userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            var errors = createResult.Errors
                .GroupBy(e => e.Code)
                .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());

            return Results.ValidationProblem(errors, 
                detail: "User registration failed.", 
                instance: httpContext.Request.Path);
        }

        await userManager.AddToRoleAsync(user, "Customer");

        var response = new RegisterResponse(user.Id, user.Email, user.FirstName, user.LastName);
        return Results.Created($"/api/v1/identity/me", response);
    }

    private static async Task<IResult> LoginUser(
        LoginRequest request,
        IValidator<LoginRequest> validator,
        UserManager<ApplicationUser> userManager,
        ITokenService tokenService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return Results.ValidationProblem(validationResult.ToDictionary(), 
                detail: "One or more validation errors occurred.", 
                instance: httpContext.Request.Path);
        }

        var user = await userManager.FindByEmailAsync(request.Email);
        if (user == null || !await userManager.CheckPasswordAsync(user, request.Password))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized",
                detail: "Invalid credentials.",
                instance: httpContext.Request.Path,
                type: "https://shopsphere.local/errors/unauthorized");
        }

        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
        var tokens = await tokenService.CreateTokensAsync(user, ipAddress, cancellationToken);
        return Results.Ok(tokens);
    }

    private static async Task<IResult> RefreshToken(
        RefreshTokenRequest request,
        IValidator<RefreshTokenRequest> validator,
        ITokenService tokenService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return Results.ValidationProblem(validationResult.ToDictionary(), 
                detail: "One or more validation errors occurred.", 
                instance: httpContext.Request.Path);
        }

        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
        var result = await tokenService.RotateRefreshTokenAsync(request.RefreshToken, ipAddress, cancellationToken);

        if (!result.Succeeded)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized",
                detail: result.ErrorMessage ?? "Invalid refresh token.",
                instance: httpContext.Request.Path,
                type: "https://shopsphere.local/errors/unauthorized");
        }

        return Results.Ok(result.Tokens);
    }

    private static async Task<IResult> RevokeToken(
        RevokeTokenRequest request,
        IValidator<RevokeTokenRequest> validator,
        ITokenService tokenService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return Results.ValidationProblem(validationResult.ToDictionary(), 
                detail: "One or more validation errors occurred.", 
                instance: httpContext.Request.Path);
        }

        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
        await tokenService.RevokeRefreshTokenAsync(request.RefreshToken, ipAddress, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> GetCurrentUser(
        UserManager<ApplicationUser> userManager,
        HttpContext httpContext)
    {
        var userId = httpContext.User.GetUserId();
        if (userId == null)
        {
            return Results.Unauthorized();
        }

        var user = await userManager.FindByIdAsync(userId.Value.ToString());
        if (user == null)
        {
            return Results.NotFound();
        }

        var roles = await userManager.GetRolesAsync(user);
        var profile = new UserProfileResponse(
            user.Id,
            user.Email ?? string.Empty,
            user.FirstName,
            user.LastName,
            roles.ToArray());

        return Results.Ok(profile);
    }
}

