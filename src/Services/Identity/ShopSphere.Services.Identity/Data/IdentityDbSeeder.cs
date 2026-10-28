using Microsoft.AspNetCore.Identity;
using ShopSphere.Services.Identity.Data.Entities;

namespace ShopSphere.Services.Identity.Data;

public static class IdentityDbSeeder
{
    private static readonly string[] DefaultRoles = ["Customer", "Administrator", "Admin"];

    public static async Task SeedRolesAsync(RoleManager<ApplicationRole> roleManager, ILogger logger)
    {
        foreach (var roleName in DefaultRoles)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                var role = new ApplicationRole(roleName);
                var result = await roleManager.CreateAsync(role);
                if (result.Succeeded)
                {
                    logger.LogInformation("Seeded default role: {RoleName}", roleName);
                }
                else
                {
                    logger.LogError("Failed to seed role {RoleName}: {Errors}", 
                        roleName, 
                        string.Join(", ", result.Errors.Select(e => e.Description)));
                }
            }
        }
    }
}

