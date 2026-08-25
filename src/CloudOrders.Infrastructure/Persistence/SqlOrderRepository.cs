using CloudOrders.Application.Abstractions;
using CloudOrders.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace CloudOrders.Infrastructure.Persistence;

public sealed class SqlOrderRepository(IDbContextFactory<CloudOrdersDbContext> contextFactory) : IOrderRepository
{
    public async Task AddAsync(Order order, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        context.Orders.Add(OrderPersistenceMapper.ToEntity(order));
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<Order?> GetAsync(Guid orderId, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.Orders
            .AsNoTracking()
            .SingleOrDefaultAsync(order => order.Id == orderId, cancellationToken);
        return entity is null ? null : OrderPersistenceMapper.ToDomain(entity);
    }
}
