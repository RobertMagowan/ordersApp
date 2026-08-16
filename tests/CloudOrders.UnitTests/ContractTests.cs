using CloudOrders.Contracts.Orders;

namespace CloudOrders.UnitTests;

public sealed class ContractTests
{
    [Fact]
    public void OrderCreatedEventCarriesExplicitVersionedIdentityFields()
    {
        var eventId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var message = new OrderCreatedIntegrationEventV1(
            eventId,
            orderId,
            "CUST-001",
            "SKU-001",
            2,
            DateTimeOffset.UtcNow);

        Assert.Equal(eventId, message.EventId);
        Assert.Equal(orderId, message.OrderId);
        Assert.Equal(OrderCreatedIntegrationEventV1.CurrentMessageVersion, 1);
        Assert.Equal(OrderCreatedIntegrationEventV1.MessageType, "orders.order-created");
    }
}
