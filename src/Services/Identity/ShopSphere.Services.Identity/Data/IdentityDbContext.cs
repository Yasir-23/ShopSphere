using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ShopSphere.Services.Identity.Data.Entities;

namespace ShopSphere.Services.Identity.Data;

public class IdentityDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>
{
    public IdentityDbContext(DbContextOptions<IdentityDbContext> options) : base(options)
    {
    }

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Map ASP.NET Identity tables
        builder.Entity<ApplicationUser>(b =>
        {
            b.ToTable("Users");
            b.Property(u => u.FirstName).IsRequired().HasMaxLength(100);
            b.Property(u => u.LastName).IsRequired().HasMaxLength(100);
            b.Property(u => u.CreatedAtUtc).IsRequired();
            b.Property(u => u.UpdatedAtUtc);
        });

        builder.Entity<ApplicationRole>(b =>
        {
            b.ToTable("Roles");
        });

        builder.Entity<IdentityUserRole<Guid>>(b =>
        {
            b.ToTable("UserRoles");
        });

        builder.Entity<IdentityUserClaim<Guid>>(b =>
        {
            b.ToTable("UserClaims");
        });

        builder.Entity<IdentityUserLogin<Guid>>(b =>
        {
            b.ToTable("UserLogins");
        });

        builder.Entity<IdentityUserToken<Guid>>(b =>
        {
            b.ToTable("UserTokens");
        });

        builder.Entity<IdentityRoleClaim<Guid>>(b =>
        {
            b.ToTable("RoleClaims");
        });

        // Map Refresh Tokens
        builder.Entity<RefreshToken>(b =>
        {
            b.ToTable("RefreshTokens");
            b.HasKey(r => r.Id);

            b.Property(r => r.TokenHash).IsRequired().HasMaxLength(256);
            b.Property(r => r.CreatedByIp).HasMaxLength(100);
            b.Property(r => r.RevokedByIp).HasMaxLength(100);
            b.Property(r => r.ReplacedByTokenHash).HasMaxLength(256);

            b.Ignore(r => r.IsExpired);
            b.Ignore(r => r.IsActive);

            b.HasIndex(r => r.TokenHash)
                .HasDatabaseName("IX_RefreshTokens_TokenHash");

            b.HasIndex(r => r.UserId)
                .HasDatabaseName("IX_RefreshTokens_UserId");

            b.HasOne(r => r.User)
                .WithMany(u => u.RefreshTokens)
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

