using System.Collections.Concurrent;
using CloudOrders.Application.Abstractions;
using CloudOrders.Contracts.Orders;

namespace CloudOrders.Infrastructure.InMemory;

public sealed class InMemoryOutboxWriter : IOutboxWriter
{
    private readonly ConcurrentBag<OrderCreatedIntegrationEventV1> events = [];

    public Task AddAsync(OrderCreatedIntegrationEventV1 integrationEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        events.Add(integrationEvent);
        return Task.CompletedTask;
    }
}
