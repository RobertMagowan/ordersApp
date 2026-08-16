using System.Collections.Concurrent;
using CloudOrders.Application.Abstractions;
using CloudOrders.Domain.Orders;

namespace CloudOrders.Infrastructure.InMemory;

public sealed class InMemoryOrderRepository : IOrderRepository
{
    private readonly ConcurrentDictionary<Guid, Order> orders = new();

    public Task AddAsync(Order order, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!orders.TryAdd(order.Id, order))
        {
            throw new InvalidOperationException($"Order {order.Id} already exists.");
        }

        return Task.CompletedTask;
    }

    public Task<Order?> GetAsync(Guid orderId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        orders.TryGetValue(orderId, out var order);
        return Task.FromResult(order);
    }
}
