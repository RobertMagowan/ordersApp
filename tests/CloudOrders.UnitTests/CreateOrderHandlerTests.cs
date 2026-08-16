using CloudOrders.Application.Abstractions;
using CloudOrders.Application.Orders;
using CloudOrders.Contracts.Orders;
using CloudOrders.Domain.Orders;

namespace CloudOrders.UnitTests;

public sealed class CreateOrderHandlerTests
{
    [Fact]
    public async Task HandlePersistsOrderAndOutboxEvent()
    {
        var repository = new RecordingOrderRepository();
        var outbox = new RecordingOutboxWriter();
        var handler = new CreateOrderHandler(repository, outbox, TimeProvider.System);

        var result = await handler.Handle(
            new CreateOrderCommand(" cust-001 ", " sku-001 ", 2),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("CUST-001", result.Value.CustomerReference);
        Assert.Single(repository.Orders);
        Assert.Single(outbox.Events);
        Assert.Equal(result.Value.Id, outbox.Events[0].OrderId);
    }

    private sealed class RecordingOrderRepository : IOrderRepository
    {
        public List<Order> Orders { get; } = [];

        public Task AddAsync(Order order, CancellationToken cancellationToken)
        {
            Orders.Add(order);
            return Task.CompletedTask;
        }

        public Task<Order?> GetAsync(Guid orderId, CancellationToken cancellationToken) =>
            Task.FromResult(Orders.SingleOrDefault(order => order.Id == orderId));
    }

    private sealed class RecordingOutboxWriter : IOutboxWriter
    {
        public List<OrderCreatedIntegrationEventV1> Events { get; } = [];

        public Task AddAsync(OrderCreatedIntegrationEventV1 integrationEvent, CancellationToken cancellationToken)
        {
            Events.Add(integrationEvent);
            return Task.CompletedTask;
        }
    }
}
