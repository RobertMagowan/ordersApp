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
    private readonly int messageVersion = CurrentMessageVersion;
    private readonly string serializedMessageType = MessageType;
    [JsonPropertyName("messageVersion")]
    public int MessageVersion => messageVersion;
    [JsonPropertyName("messageType")]
    public string SerializedMessageType => serializedMessageType;
    public string? TraceParent { get; init; }
}
