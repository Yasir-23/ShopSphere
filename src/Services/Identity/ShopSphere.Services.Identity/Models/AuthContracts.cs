namespace ShopSphere.Services.Identity.Models;

public record RegisterRequest(
    string Email,
    string Password,
    string FirstName,
    string LastName);

public record RegisterResponse(
    Guid Id,
    string Email,
    string FirstName,
    string LastName);

public record LoginRequest(
    string Email,
    string Password);

public record LoginResponse(
    string AccessToken,
    int ExpiresIn,
    string RefreshToken,
    string TokenType = "Bearer");

public record RefreshTokenRequest(
    string RefreshToken);

public record RevokeTokenRequest(
    string RefreshToken);

public record UserProfileResponse(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    IReadOnlyCollection<string> Roles);

