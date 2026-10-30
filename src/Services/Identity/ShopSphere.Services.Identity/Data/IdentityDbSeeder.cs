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

    public static async Task SeedDefaultAdminUserAsync(UserManager<ApplicationUser> userManager, ILogger logger)
    {
        const string adminEmail = "admin@shopsphere.local";
        var existingAdmin = await userManager.FindByEmailAsync(adminEmail);

        if (existingAdmin is null)
        {
            var adminUser = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                FirstName = "Admin",
                LastName = "User",
                EmailConfirmed = true
            };

            var createResult = await userManager.CreateAsync(adminUser, "AdminPassword123!");
            if (createResult.Succeeded)
            {
                logger.LogInformation("Successfully created default administrator user: {Email}", adminEmail);
                var roleResult = await userManager.AddToRolesAsync(adminUser, new[] { "Administrator", "Admin" });
                if (roleResult.Succeeded)
                {
                    logger.LogInformation("Assigned 'Administrator' and 'Admin' roles to: {Email}", adminEmail);
                }
                else
                {
                    logger.LogError("Failed to assign roles to default admin user: {Errors}",
                        string.Join(", ", roleResult.Errors.Select(e => e.Description)));
                }
            }
            else
            {
                logger.LogError("Failed to create default admin user: {Errors}",
                    string.Join(", ", createResult.Errors.Select(e => e.Description)));
            }
        }
        else
        {
            logger.LogInformation("Default administrator user already exists: {Email}", adminEmail);
        }
    }
}
