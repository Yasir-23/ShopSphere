using Microsoft.EntityFrameworkCore;
using ShopSphere.Services.Payment.Entities;

namespace ShopSphere.Services.Payment.Data;

public class PaymentDbContext : DbContext
{
    public PaymentDbContext(DbContextOptions<PaymentDbContext> options) : base(options)
    {
    }

    public DbSet<PaymentTransaction> Transactions => Set<PaymentTransaction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<PaymentTransaction>(builder =>
        {
            builder.ToTable("payment_transactions");

            builder.HasKey(t => t.Id);

            builder.HasIndex(t => t.OrderId)
                .IsUnique();

            builder.Property(t => t.Amount)
                .HasPrecision(18, 2)
                .IsRequired();

            builder.Property(t => t.Currency)
                .HasMaxLength(10)
                .HasDefaultValue("USD")
                .IsRequired();

            builder.Property(t => t.PaymentMethod)
                .HasMaxLength(100)
                .IsRequired();

            builder.Property(t => t.TransactionId)
                .HasMaxLength(100);

            builder.Property(t => t.FailureReason)
                .HasMaxLength(500);

            builder.Property(t => t.Status)
                .HasConversion<int>()
                .IsRequired();

            builder.Property(t => t.CreatedAtUtc)
                .IsRequired();
        });
    }
}

