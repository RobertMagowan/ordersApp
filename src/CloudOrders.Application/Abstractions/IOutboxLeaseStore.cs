namespace CloudOrders.Application.Abstractions;

public interface IOutboxLeaseStore
{
    Task<IReadOnlyList<OutboxMessageLease>> ClaimAsync(string leaseOwner, int batchSize, TimeSpan leaseDuration, CancellationToken cancellationToken);
    Task<bool> MarkPublishedAsync(Guid eventId, string leaseOwner, Guid leaseToken, CancellationToken cancellationToken);
    Task<bool> RenewAsync(Guid eventId, string leaseOwner, Guid leaseToken, TimeSpan leaseDuration, CancellationToken cancellationToken);
    Task<OutboxMetrics> GetMetricsAsync(CancellationToken cancellationToken);
}

public sealed record OutboxMessageLease(
    Guid EventId,
    Guid OrderId,
    string MessageType,
    int MessageVersion,
    string Payload,
    string? TraceParent,
    int AttemptCount,
    string LeaseOwner,
    Guid LeaseToken,
    DateTimeOffset LeaseExpiresAt);

public sealed record OutboxMetrics(int PendingCount, TimeSpan OldestPendingAge);
