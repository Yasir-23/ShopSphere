using Microsoft.EntityFrameworkCore;
using ShopSphere.Services.Inventory.Entities;

namespace ShopSphere.Services.Inventory.Data;

public class InventoryDbContext : DbContext
{
    public InventoryDbContext(DbContextOptions<InventoryDbContext> options) : base(options)
    {
    }

    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<InventoryReservation> InventoryReservations => Set<InventoryReservation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<InventoryItem>(entity =>
        {
            entity.ToTable("inventory_items");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.ProductId)
                .IsRequired()
                .HasMaxLength(64);

            entity.Property(e => e.Sku)
                .IsRequired()
                .HasMaxLength(64);

            entity.HasIndex(e => e.ProductId)
                .IsUnique();

            entity.HasIndex(e => e.Sku)
                .IsUnique();

            entity.Property(e => e.QuantityOnHand)
                .IsRequired();

            entity.Property(e => e.QuantityReserved)
                .IsRequired();

            entity.Property(e => e.RestockThreshold)
                .IsRequired();

            entity.Ignore(e => e.QuantityAvailable);

            entity.Property(e => e.RowVersion)
                .IsConcurrencyToken()
                .IsRequired();

            entity.Property(e => e.CreatedAtUtc)
                .IsRequired();

            entity.Property(e => e.UpdatedAtUtc)
                .IsRequired();
        });

        modelBuilder.Entity<InventoryReservation>(entity =>
        {
            entity.ToTable("inventory_reservations");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.OrderId)
                .IsRequired();

            entity.HasIndex(e => e.OrderId);

            entity.Property(e => e.ProductId)
                .IsRequired()
                .HasMaxLength(64);

            entity.Property(e => e.Quantity)
                .IsRequired();

            entity.Property(e => e.ReservedAtUtc)
                .IsRequired();

            entity.HasOne(e => e.InventoryItem)
                .WithMany()
                .HasForeignKey(e => e.InventoryItemId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<InventoryItem>())
        {
            if (entry.State == EntityState.Added && (entry.Entity.RowVersion == null || entry.Entity.RowVersion.Length == 0))
            {
                entry.Entity.RowVersion = Guid.NewGuid().ToByteArray();
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAtUtc = DateTime.UtcNow;
                entry.Entity.RowVersion = Guid.NewGuid().ToByteArray();
            }
        }

        return await base.SaveChangesAsync(cancellationToken);
    }
}

