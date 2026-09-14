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

    private static OutboxMessageLease Lease() => CreateLease();
    private static OutboxMessageLease CreateLease() => new(Guid.NewGuid(), Guid.NewGuid(), "orders.order-created", 1, "{\"eventId\":\"persisted\"}", "trace", 1, "publisher", Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(1));

    private sealed class FakeStore(OutboxMessageLease message) : IOutboxLeaseStore
    {
        private int claims;
        public bool Marked { get; private set; }
        public bool MarkResult { get; set; } = true;
        public Task<IReadOnlyList<OutboxMessageLease>> ClaimAsync(string owner, int batchSize, TimeSpan duration, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<OutboxMessageLease>>(Interlocked.Increment(ref claims) == 1 ? [message] : []);
        public Task<bool> MarkPublishedAsync(Guid eventId, string owner, Guid token, CancellationToken cancellationToken) { Marked = MarkResult; return Task.FromResult(MarkResult); }
        public Task<bool> RenewAsync(Guid eventId, string owner, Guid token, TimeSpan duration, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class RecordingSender : IOutboxMessageSender
    {
        public Exception? Failure { get; init; }
        public Guid MessageId { get; private set; }
        public string? Payload { get; private set; }
        public Task SendAsync(OutboxMessageLease message, CancellationToken cancellationToken)
        { if (Failure is not null) throw Failure; MessageId = message.EventId; Payload = message.Payload; return Task.CompletedTask; }
    }

    private sealed class TestTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow;
    }
}
