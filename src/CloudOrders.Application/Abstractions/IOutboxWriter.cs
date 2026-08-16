using CloudOrders.Contracts.Orders;

namespace CloudOrders.Application.Abstractions;

public interface IOutboxWriter
{
    Task AddAsync(OrderCreatedIntegrationEventV1 integrationEvent, CancellationToken cancellationToken);
}
