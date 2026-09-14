using CloudOrders.Application.Abstractions;
using CloudOrders.OutboxPublisher;

namespace CloudOrders.IntegrationTests;

public sealed class OutboxPublisherIntegrationTests
{
    [Fact]
    public async Task SendsPersistedPayloadWithOriginalEventIdThenMarksPublished()
    {
        var message = Lease();
        var store = new FakeStore(message);
        var sender = new RecordingSender();
        var publisher = new OutboxPublisherFunction(store, sender, new TestTimeProvider());

        var result = await publisher.DrainAsync(CancellationToken.None);

        Assert.Equal(1, result.Published);
        Assert.Equal(message.Payload, sender.Payload);
        Assert.Equal(message.EventId, sender.MessageId);
        Assert.True(store.Marked);
        Assert.True(store.Renewed);
    }

    [Fact]
    public async Task BrokerFailureLeavesRowPending()
    {
        var store = new FakeStore(Lease());
        var publisher = new OutboxPublisherFunction(store, new RecordingSender { Failure = new InvalidOperationException("broker") }, new TestTimeProvider());

        var result = await publisher.DrainAsync(CancellationToken.None);

        Assert.Equal(1, result.Failed);
        Assert.False(store.Marked);
    }

    [Fact]
    public async Task StaleTokenMarkIsReportedSeparately()
    {
        var store = new FakeStore(CreateLease()) { MarkResult = false };
        var publisher = new OutboxPublisherFunction(store, new RecordingSender(), new TestTimeProvider());

        var result = await publisher.DrainAsync(CancellationToken.None);

        Assert.Equal(1, result.StaleToken);
        Assert.Equal(0, result.Failed);
    }

    [Fact]
    public async Task SendCancellationRetainsLeaseAndReportsDeadline()
    {
        var store = new FakeStore(Lease());
        var publisher = new OutboxPublisherFunction(store, new RecordingSender { WaitForCancellation = true }, new DeadlineTimeProvider());

        var result = await publisher.DrainAsync(CancellationToken.None);

        Assert.Equal(1, result.Failed);
        Assert.True(result.DeadlineReached);
        Assert.False(store.Marked);
    }

    [Fact]
    public async Task CrashAfterSendIsReclaimedAndRepublishedWithSamePayloadAndEventId()
    {
        var first = Lease();
        var second = first with { LeaseToken = Guid.NewGuid(), LeaseExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1) };
        var store = new CrashReclaimStore(first, second);
        var sender = new RecordingSender();

        await new OutboxPublisherFunction(store, sender, new TestTimeProvider()).DrainAsync(CancellationToken.None);
        await new OutboxPublisherFunction(store, sender, new TestTimeProvider()).DrainAsync(CancellationToken.None);

        Assert.Equal(2, sender.Sends.Count);
        Assert.All(sender.Sends, send => Assert.Equal((first.EventId, first.Payload), send));
        Assert.True(store.Marked);
    }

    private static OutboxMessageLease Lease() => CreateLease();
    private static OutboxMessageLease CreateLease() => new(Guid.NewGuid(), Guid.NewGuid(), "orders.order-created", 1, "{\"eventId\":\"persisted\"}", "trace", 1, "publisher", Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(1));

    private sealed class FakeStore(OutboxMessageLease message) : IOutboxLeaseStore
    {
        private int claims;
        public bool Marked { get; private set; }
        public bool Renewed { get; private set; }
        public bool MarkResult { get; set; } = true;
        public OutboxMetrics Metrics { get; set; } = new(1, TimeSpan.FromMinutes(2));
        public Task<IReadOnlyList<OutboxMessageLease>> ClaimAsync(string owner, int batchSize, TimeSpan duration, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<OutboxMessageLease>>(Interlocked.Increment(ref claims) == 1 ? [message] : []);
        public Task<bool> MarkPublishedAsync(Guid eventId, string owner, Guid token, CancellationToken cancellationToken) { Marked = MarkResult; return Task.FromResult(MarkResult); }
        public Task<bool> RenewAsync(Guid eventId, string owner, Guid token, TimeSpan duration, CancellationToken cancellationToken) { Renewed = true; return Task.FromResult(true); }
        public Task<OutboxMetrics> GetMetricsAsync(CancellationToken cancellationToken) => Task.FromResult(Metrics);
    }

    private sealed class RecordingSender : IOutboxMessageSender
    {
        public Exception? Failure { get; init; }
        public bool Cancel { get; init; }
        public bool WaitForCancellation { get; init; }
        public Guid MessageId { get; private set; }
        public string? Payload { get; private set; }
        public List<(Guid EventId, string Payload)> Sends { get; } = [];
        public Task SendAsync(OutboxMessageLease message, CancellationToken cancellationToken)
        { if (Failure is not null) throw Failure; if (Cancel) throw new OperationCanceledException(cancellationToken); if (WaitForCancellation) return WaitAsync(cancellationToken); MessageId = message.EventId; Payload = message.Payload; Sends.Add((message.EventId, message.Payload)); return Task.CompletedTask; }
        private static async Task WaitAsync(CancellationToken token) => await Task.Delay(Timeout.InfiniteTimeSpan, token);
    }

    private sealed class CrashReclaimStore(OutboxMessageLease first, OutboxMessageLease second) : IOutboxLeaseStore
    {
        private int claims;
        public bool Marked { get; private set; }
        public Task<IReadOnlyList<OutboxMessageLease>> ClaimAsync(string owner, int batchSize, TimeSpan duration, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<OutboxMessageLease>>(Interlocked.Increment(ref claims) switch { 1 => [first], 2 => [second], _ => [] });
        public Task<bool> MarkPublishedAsync(Guid eventId, string owner, Guid token, CancellationToken cancellationToken)
        { if (claims == 1) throw new InvalidOperationException("process terminated after send"); Marked = true; return Task.FromResult(true); }
        public Task<bool> RenewAsync(Guid eventId, string owner, Guid token, TimeSpan duration, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<OutboxMetrics> GetMetricsAsync(CancellationToken cancellationToken) => Task.FromResult(new OutboxMetrics(Marked ? 0 : 1, TimeSpan.FromMinutes(1)));
    }

    private sealed class TestTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow;
    }

    private sealed class DeadlineTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset start = DateTimeOffset.UtcNow;
        private int calls;
        public override DateTimeOffset GetUtcNow() => Interlocked.Increment(ref calls) >= 5 ? start.Add(OutboxPublisherFunction.DrainDuration).Subtract(TimeSpan.FromMilliseconds(1)) : start;
    }
}
