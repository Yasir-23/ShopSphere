using ShopSphere.Services.Identity.Data.Entities;
using ShopSphere.Services.Identity.Models;

namespace ShopSphere.Services.Identity.Services;

public record TokenRefreshResult(
    bool Succeeded,
    LoginResponse? Tokens = null,
    string? ErrorMessage = null,
    bool IsCompromised = false);

public interface ITokenService
{
    Task<LoginResponse> CreateTokensAsync(ApplicationUser user, string ipAddress, CancellationToken cancellationToken = default);
    Task<TokenRefreshResult> RotateRefreshTokenAsync(string rawRefreshToken, string ipAddress, CancellationToken cancellationToken = default);
    Task<bool> RevokeRefreshTokenAsync(string rawRefreshToken, string ipAddress, CancellationToken cancellationToken = default);
}

