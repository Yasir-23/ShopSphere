using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopSphere.Services.Ordering.Domain.Entities;

namespace ShopSphere.Services.Ordering.Infrastructure.Persistence.Configurations;

public class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("OrderItems");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.OrderId)
            .IsRequired();

        builder.Property(i => i.ProductId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(i => i.ProductName)
            .IsRequired()
            .HasMaxLength(250);

        builder.Property(i => i.UnitPrice)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(i => i.Quantity)
            .IsRequired();

        builder.Ignore(i => i.TotalPrice);
    }
}

