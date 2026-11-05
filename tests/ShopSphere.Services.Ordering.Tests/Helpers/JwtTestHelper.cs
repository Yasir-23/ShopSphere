using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace ShopSphere.Services.Ordering.Tests.Helpers;

public static class JwtTestHelper
{
    public const string SecretKey = "ShopSphere_Super_Secret_Jwt_Signing_Key_2026_Must_Be_Long_Enough!";
    public const string Issuer = "ShopSphere";
    public const string Audience = "ShopSphere.Clients";

    public static string GenerateToken(
        string userId,
        string email = "test.customer@shopsphere.io",
        string role = "Customer")
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(SecretKey);
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim("sub", userId),
                new Claim("userId", userId),
                new Claim(ClaimTypes.Email, email),
                new Claim(ClaimTypes.Role, role)
            }),
            Expires = DateTime.UtcNow.AddHours(2),
            Issuer = Issuer,
            Audience = Audience,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }
}

