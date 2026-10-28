using Microsoft.AspNetCore.Identity;

namespace ShopSphere.Services.Identity.Data.Entities;

public class ApplicationRole : IdentityRole<Guid>
{
    public ApplicationRole() : base()
    {
    }

    public ApplicationRole(string roleName) : base(roleName)
    {
    }
}

