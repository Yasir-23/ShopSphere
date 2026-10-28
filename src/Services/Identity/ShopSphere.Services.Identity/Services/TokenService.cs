using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ShopSphere.Common.Security;
using ShopSphere.Services.Identity.Data;
using ShopSphere.Services.Identity.Data.Entities;
using ShopSphere.Services.Identity.Models;

namespace ShopSphere.Services.Identity.Services;

public class TokenService : ITokenService
{
    private readonly IdentityDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly JwtOptions _jwtOptions;
    private readonly ILogger<TokenService> _logger;

    public TokenService(
        IdentityDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        IOptions<JwtOptions> jwtOptions,
        ILogger<TokenService> logger)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _jwtOptions = jwtOptions.Value;
        _logger = logger;
    }

    public async Task<LoginResponse> CreateTokensAsync(
        ApplicationUser user,
        string ipAddress,
        CancellationToken cancellationToken = default)
    {
        var roles = await _userManager.GetRolesAsync(user);
        var accessToken = GenerateAccessToken(user, roles);
        var rawRefreshToken = GenerateSecureToken();
        var tokenHash = HashToken(rawRefreshToken);

        var expirationDays = _jwtOptions.RefreshTokenExpirationDays > 0 
            ? _jwtOptions.RefreshTokenExpirationDays 
            : 7;

        var refreshToken = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = tokenHash,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(expirationDays),
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByIp = ipAddress
        };

        _dbContext.RefreshTokens.Add(refreshToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var expiresIn = (_jwtOptions.AccessTokenExpirationMinutes > 0 
            ? _jwtOptions.AccessTokenExpirationMinutes 
            : 15) * 60;

        return new LoginResponse(accessToken, expiresIn, rawRefreshToken);
    }

    public async Task<TokenRefreshResult> RotateRefreshTokenAsync(
        string rawRefreshToken,
        string ipAddress,
        CancellationToken cancellationToken = default)
    {
        var tokenHash = HashToken(rawRefreshToken);

        var tokenRecord = await _dbContext.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

        if (tokenRecord == null)
        {
            return new TokenRefreshResult(false, ErrorMessage: "Invalid refresh token.");
        }

        // Reuse detection: If revoked token is presented, revoke all active tokens for that user family
        if (tokenRecord.RevokedAtUtc != null)
        {
            _logger.LogWarning("Refresh token reuse detected for user {UserId}! Revoking entire token family.", tokenRecord.UserId);

            var activeTokens = await _dbContext.RefreshTokens
                .Where(t => t.UserId == tokenRecord.UserId && t.RevokedAtUtc == null)
                .ToListAsync(cancellationToken);

            foreach (var active in activeTokens)
            {
                active.RevokedAtUtc = DateTime.UtcNow;
                active.RevokedByIp = ipAddress;
                active.ReplacedByTokenHash = "RevokedDueToReuseDetection";
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            return new TokenRefreshResult(false, ErrorMessage: "Compromised refresh token reused. All active sessions have been terminated.", IsCompromised: true);
        }

        // Check expiration
        if (tokenRecord.IsExpired)
        {
            tokenRecord.RevokedAtUtc = DateTime.UtcNow;
            tokenRecord.RevokedByIp = ipAddress;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return new TokenRefreshResult(false, ErrorMessage: "Refresh token has expired.");
        }

        var user = tokenRecord.User ?? await _userManager.FindByIdAsync(tokenRecord.UserId.ToString());
        if (user == null)
        {
            return new TokenRefreshResult(false, ErrorMessage: "User no longer exists.");
        }

        // Rotate: Invalidate current token and issue new token pair
        var newRawToken = GenerateSecureToken();
        var newHash = HashToken(newRawToken);

        tokenRecord.RevokedAtUtc = DateTime.UtcNow;
        tokenRecord.RevokedByIp = ipAddress;
        tokenRecord.ReplacedByTokenHash = newHash;

        var expirationDays = _jwtOptions.RefreshTokenExpirationDays > 0 
            ? _jwtOptions.RefreshTokenExpirationDays 
            : 7;

        var newRefreshToken = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = newHash,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(expirationDays),
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByIp = ipAddress
        };

        _dbContext.RefreshTokens.Add(newRefreshToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var roles = await _userManager.GetRolesAsync(user);
        var accessToken = GenerateAccessToken(user, roles);
        var expiresIn = (_jwtOptions.AccessTokenExpirationMinutes > 0 
            ? _jwtOptions.AccessTokenExpirationMinutes 
            : 15) * 60;

        return new TokenRefreshResult(true, new LoginResponse(accessToken, expiresIn, newRawToken));
    }

    public async Task<bool> RevokeRefreshTokenAsync(
        string rawRefreshToken,
        string ipAddress,
        CancellationToken cancellationToken = default)
    {
        var tokenHash = HashToken(rawRefreshToken);

        var tokenRecord = await _dbContext.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

        if (tokenRecord == null || tokenRecord.RevokedAtUtc != null)
        {
            return false;
        }

        tokenRecord.RevokedAtUtc = DateTime.UtcNow;
        tokenRecord.RevokedByIp = ipAddress;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }

    private string GenerateAccessToken(ApplicationUser user, IList<string> roles)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(ClaimTypes.Email, user.Email ?? string.Empty),
            new(JwtRegisteredClaimNames.GivenName, user.FirstName),
            new(ClaimTypes.GivenName, user.FirstName),
            new(JwtRegisteredClaimNames.FamilyName, user.LastName),
            new(ClaimTypes.Surname, user.LastName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        foreach (var role in roles)
        {
            claims.Add(new Claim("role", role));
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var secretKey = string.IsNullOrWhiteSpace(_jwtOptions.SecretKey)
            ? "ShopSphere_Default_Insecure_Development_SecretKey_Must_Be_32_Bytes_Long_2026!"
            : _jwtOptions.SecretKey;

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var expirationMinutes = _jwtOptions.AccessTokenExpirationMinutes > 0 
            ? _jwtOptions.AccessTokenExpirationMinutes 
            : 15;

        var token = new JwtSecurityToken(
            issuer: _jwtOptions.Issuer,
            audience: _jwtOptions.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expirationMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string GenerateSecureToken()
    {
        var randomBytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(randomBytes);
    }

    private static string HashToken(string token)
    {
        var bytes = Encoding.UTF8.GetBytes(token);
        var hashBytes = SHA512.HashData(bytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}

