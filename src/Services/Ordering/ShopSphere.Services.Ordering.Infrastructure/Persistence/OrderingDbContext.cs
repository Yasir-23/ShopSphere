using System.Reflection;
using Microsoft.EntityFrameworkCore;
using ShopSphere.Services.Ordering.Domain.Entities;
using ShopSphere.Services.Ordering.Infrastructure.Sagas;

namespace ShopSphere.Services.Ordering.Infrastructure.Persistence;

public class OrderingDbContext : DbContext
{
    public OrderingDbContext(DbContextOptions<OrderingDbContext> options) : base(options)
    {
    }

    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderStateData> OrderStateData => Set<OrderStateData>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        modelBuilder.ApplyConfiguration(new OrderStateMap());
    }
}
