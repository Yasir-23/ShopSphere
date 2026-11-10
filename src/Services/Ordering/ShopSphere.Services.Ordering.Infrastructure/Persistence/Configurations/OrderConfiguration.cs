using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopSphere.Services.Ordering.Domain.Entities;

namespace ShopSphere.Services.Ordering.Infrastructure.Persistence.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.CustomerId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(o => o.CustomerEmail)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(o => o.TotalAmount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(o => o.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(o => o.CreatedAtUtc)
            .IsRequired();

        builder.Property(o => o.UpdatedAtUtc);

        builder.Property(o => o.CancellationReason)
            .HasMaxLength(500);

        builder.OwnsOne(o => o.ShippingAddress, address =>
        {
            address.Property(a => a.Street)
                .HasColumnName("ShippingAddress_Street")
                .HasMaxLength(200)
                .IsRequired();

            address.Property(a => a.City)
                .HasColumnName("ShippingAddress_City")
                .HasMaxLength(100)
                .IsRequired();

            address.Property(a => a.State)
                .HasColumnName("ShippingAddress_State")
                .HasMaxLength(100)
                .IsRequired();

            address.Property(a => a.ZipCode)
                .HasColumnName("ShippingAddress_ZipCode")
                .HasMaxLength(20)
                .IsRequired();

            address.Property(a => a.Country)
                .HasColumnName("ShippingAddress_Country")
                .HasMaxLength(100)
                .IsRequired();
        });

        builder.HasMany(o => o.Items)
            .WithOne()
            .HasForeignKey(i => i.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(o => o.Items)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

