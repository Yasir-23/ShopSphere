using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ShopSphere.Services.Ordering.Infrastructure.Sagas;

public class OrderStateMap : SagaClassMap<OrderStateData>, IEntityTypeConfiguration<OrderStateData>
{
    public void Configure(EntityTypeBuilder<OrderStateData> entity)
    {
        Configure(entity, null!);
    }

    protected override void Configure(EntityTypeBuilder<OrderStateData> entity, ModelBuilder model)
    {
        entity.HasKey(x => x.CorrelationId);
        entity.ToTable("OrderStateData");
        entity.Property(x => x.CurrentState).HasMaxLength(64).IsRequired();
        entity.Property(x => x.OrderId).IsRequired();
        entity.Property(x => x.CustomerId).HasMaxLength(100);
        entity.Property(x => x.TotalAmount).HasPrecision(18, 2);
        entity.Property(x => x.FailureReason).HasMaxLength(500);
        entity.Property(x => x.ItemsJson);

        entity.Ignore(x => x.Items);

        entity.Property(x => x.RowVersion).IsRowVersion();
    }
}
