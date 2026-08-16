namespace CloudOrders.Contracts.Orders;

public sealed record OrderCreatedIntegrationEventV1(
    Guid EventId,
    Guid OrderId,
    string CustomerReference,
    string ProductSku,
    int Quantity,
    DateTimeOffset OccurredAt)
{
    public const int CurrentMessageVersion = 1;
    public const string MessageType = "orders.order-created";
}
