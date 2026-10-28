using System.Security.Claims;

namespace ShopSphere.Common.Security;

public static class ClaimsPrincipalExtensions
{
    public static Guid? GetUserId(this ClaimsPrincipal principal)
    {
        var idClaim = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal.FindFirst("sub")?.Value
            ?? principal.FindFirst("userId")?.Value;

        return Guid.TryParse(idClaim, out var userId) ? userId : null;
    }

    public static Guid GetRequiredUserId(this ClaimsPrincipal principal)
    {
        return principal.GetUserId() 
            ?? throw new InvalidOperationException("User ID claim is missing or invalid in the current security principal.");
    }

    public static string? GetEmail(this ClaimsPrincipal principal)
    {
        return principal.FindFirst(ClaimTypes.Email)?.Value
            ?? principal.FindFirst("email")?.Value;
    }

    public static IReadOnlyCollection<string> GetRoles(this ClaimsPrincipal principal)
    {
        return principal.FindAll(ClaimTypes.Role)
            .Concat(principal.FindAll("role"))
            .Select(c => c.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static bool HasRole(this ClaimsPrincipal principal, string role)
    {
        return principal.IsInRole(role) || principal.HasClaim(c => 
            (c.Type == ClaimTypes.Role || c.Type == "role") && 
            string.Equals(c.Value, role, StringComparison.OrdinalIgnoreCase));
    }
}
