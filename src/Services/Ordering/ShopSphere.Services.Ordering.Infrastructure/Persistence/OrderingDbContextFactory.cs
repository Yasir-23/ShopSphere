using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ShopSphere.Services.Ordering.Infrastructure.Persistence;

public class OrderingDbContextFactory : IDesignTimeDbContextFactory<OrderingDbContext>
{
    public OrderingDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<OrderingDbContext>();
        optionsBuilder.UseSqlServer("Server=localhost,11433;Database=ShopSphere_Ordering;User Id=sa;Password=ShopSphere_Sql2022!;TrustServerCertificate=True;MultipleActiveResultSets=True;");

        return new OrderingDbContext(optionsBuilder.Options);
    }
}

