namespace CloudOrders.Infrastructure.Persistence;

internal sealed class OutboxMessageEntity
{
    public Guid EventId { get; set; }

    public Guid OrderId { get; set; }

    public Guid AggregateId { get; set; }

    public required string MessageType { get; set; }

    public int MessageVersion { get; set; }

    public required string Payload { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ProcessedAt { get; set; }

    public int AttemptCount { get; set; }

    public DateTimeOffset? LastAttemptAt { get; set; }

    public string? LastErrorCode { get; set; }

    public string? TraceParent { get; set; }

    public OrderEntity? Order { get; set; }
}
