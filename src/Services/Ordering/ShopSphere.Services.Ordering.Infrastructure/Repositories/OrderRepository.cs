using Microsoft.EntityFrameworkCore;
using ShopSphere.Services.Ordering.Application.Repositories;
using ShopSphere.Services.Ordering.Domain.Entities;
using ShopSphere.Services.Ordering.Infrastructure.Persistence;

namespace ShopSphere.Services.Ordering.Infrastructure.Repositories;

public class OrderRepository : IOrderRepository
{
    private readonly OrderingDbContext _dbContext;

    public OrderRepository(OrderingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
    }

    public async Task<(IReadOnlyList<Order> Items, int TotalCount)> GetByCustomerIdAsync(
        string customerId,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Orders
            .Include(o => o.Items)
            .Where(o => o.CustomerId == customerId);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(o => o.CreatedAtUtc)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task AddAsync(Order order, CancellationToken cancellationToken = default)
    {
        await _dbContext.Orders.AddAsync(order, cancellationToken);
    }

    public Task UpdateAsync(Order order, CancellationToken cancellationToken = default)
    {
        _dbContext.Orders.Update(order);
        return Task.CompletedTask;
    }

    public async Task<bool> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.SaveChangesAsync(cancellationToken) > 0;
    }
}

