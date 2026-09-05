namespace CloudOrders.Infrastructure.Persistence;

internal sealed class IdempotencyRecordEntity
{
    public required string SubjectId { get; set; }

    public Guid IdempotencyKey { get; set; }

    public required byte[] RequestHash { get; set; }

    public Guid OrderId { get; set; }

    public int ResponseStatus { get; set; }

    public required string ResponseJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public Guid? ActorCustomerProfileId { get; set; }

    public Guid? TargetCustomerProfileId { get; set; }

    public OrderEntity? Order { get; set; }
}
