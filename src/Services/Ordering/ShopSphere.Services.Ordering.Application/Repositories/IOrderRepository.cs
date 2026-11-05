using ShopSphere.Services.Ordering.Domain.Entities;

namespace ShopSphere.Services.Ordering.Application.Repositories;

public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<Order> Items, int TotalCount)> GetByCustomerIdAsync(string customerId, int pageNumber, int pageSize, CancellationToken cancellationToken = default);
    Task AddAsync(Order order, CancellationToken cancellationToken = default);
    Task UpdateAsync(Order order, CancellationToken cancellationToken = default);
    Task<bool> SaveChangesAsync(CancellationToken cancellationToken = default);
}

