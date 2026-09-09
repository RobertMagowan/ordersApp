namespace CloudOrders.Contracts.Orders;

using System.Text.Json.Serialization;

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
    [JsonPropertyName("messageVersion")]
    public int MessageVersion { get; init; } = CurrentMessageVersion;
    [JsonPropertyName("messageType")]
    public string SerializedMessageType { get; init; } = MessageType;
    public string? TraceParent { get; init; }
}
