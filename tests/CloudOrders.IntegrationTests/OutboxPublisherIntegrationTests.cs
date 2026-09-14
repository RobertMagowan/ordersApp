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
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var store = new FakeStore(Lease()) { BeforeClaim = () => clock.Advance(OutboxPublisherFunction.DrainDuration - TimeSpan.FromSeconds(10)) };
        var sender = new RecordingSender { WaitForCancellation = true };
        using var stop = new CancellationTokenSource();
        var drain = new OutboxPublisherFunction(store, sender, clock).DrainAsync(stop.Token);

        try
        {
            await sender.SendStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            clock.Advance(TimeSpan.FromSeconds(10));
            Assert.True(sender.CancellationObserved.Task.IsCompleted);
            var result = await drain.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, result.Failed);
            Assert.True(result.DeadlineReached);
            Assert.False(store.Marked);
        }
        finally
        {
            stop.Cancel();
            try { await drain; }
            catch (OperationCanceledException) { }
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    public async Task SlowSendIsCancelledBeforeLeaseExpiryIncludingRenewalLatency(int renewalSeconds)
    {
        var start = new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);
        var clock = new ManualTimeProvider(start);
        var store = new FakeStore(CreateLease() with { LeaseExpiresAt = start + OutboxPublisherFunction.LeaseDuration })
        { BeforeRenew = () => clock.Advance(TimeSpan.FromSeconds(renewalSeconds)) };
        var sender = new RecordingSender { WaitForCancellation = true };
        using var stop = new CancellationTokenSource();
        var drain = new OutboxPublisherFunction(store, sender, clock).DrainAsync(stop.Token);

        try
        {
            await sender.SendStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            clock.Advance(TimeSpan.FromSeconds(84 - renewalSeconds));
            Assert.False(sender.CancellationObserved.Task.IsCompleted);
            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.True(sender.CancellationObserved.Task.IsCompleted);
            var result = await drain.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, result.Failed);
            Assert.False(result.DeadlineReached);
            Assert.False(store.Marked);
        }
        finally
        {
            stop.Cancel();
            try { await drain; }
            catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task ConditionalMarkSharesRemainingLeaseBudgetWithSend()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var markStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var markCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new FakeStore(Lease())
        {
            MarkOperation = async token =>
            {
                using var registration = token.Register(() => markCancelled.TrySetResult());
                markStarted.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return true;
            }
        };
        var sender = new RecordingSender { BeforeSend = () => clock.Advance(TimeSpan.FromSeconds(60)) };
        using var stop = new CancellationTokenSource();
        var drain = new OutboxPublisherFunction(store, sender, clock).DrainAsync(stop.Token);

        try
        {
            await markStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            clock.Advance(TimeSpan.FromSeconds(24));
            Assert.False(markCancelled.Task.IsCompleted);
            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.True(markCancelled.Task.IsCompleted);
            var result = await drain.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, result.Failed);
            Assert.False(result.DeadlineReached);
            Assert.False(store.Marked);
            Assert.Single(sender.Sends);
        }
        finally
        {
            stop.Cancel();
            try { await drain; }
            catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task SenderReturningAfterLeaseBudgetCannotStartConditionalMark()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var store = new FakeStore(Lease());
        var sender = new RecordingSender { BeforeSend = () => clock.Advance(TimeSpan.FromSeconds(90)) };

        var result = await new OutboxPublisherFunction(store, sender, clock).DrainAsync(CancellationToken.None);

        Assert.Equal(1, result.Failed);
        Assert.False(store.Marked);
    }

    [Fact]
    public async Task CrashAfterSendIsReclaimedAndRepublishedWithSamePayloadAndEventId()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero));
        var first = Lease();
        var store = new CrashReclaimStore(first, clock);
        var sender = new RecordingSender();

        await new OutboxPublisherFunction(store, sender, clock).DrainAsync(CancellationToken.None);
        await new OutboxPublisherFunction(store, sender, clock).DrainAsync(CancellationToken.None);
        Assert.Single(sender.Sends);

        clock.Advance(OutboxPublisherFunction.LeaseDuration - TimeSpan.FromTicks(1));
        await new OutboxPublisherFunction(store, sender, clock).DrainAsync(CancellationToken.None);
        Assert.Single(sender.Sends);
        Assert.False(store.ExpiredLeaseObserved);
        clock.Advance(TimeSpan.FromTicks(1));
        var result = await new OutboxPublisherFunction(store, sender, clock).DrainAsync(CancellationToken.None);

        Assert.Equal(2, sender.Sends.Count);
        Assert.All(sender.Sends, send => Assert.Equal((first.EventId, first.Payload), send));
        Assert.True(store.ExpiredLeaseObserved);
        Assert.Equal(2, store.LeaseTokens.Distinct().Count());
        Assert.True(store.Marked);
        Assert.Equal(1, result.Published);
        Assert.Equal(0, result.Pending);
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
        public Action? BeforeClaim { get; init; }
        public Action? BeforeRenew { get; init; }
        public Func<CancellationToken, Task<bool>>? MarkOperation { get; init; }
        public Task<IReadOnlyList<OutboxMessageLease>> ClaimAsync(string owner, int batchSize, TimeSpan duration, CancellationToken cancellationToken)
        { if (Interlocked.Increment(ref claims) != 1) return Task.FromResult<IReadOnlyList<OutboxMessageLease>>([]); BeforeClaim?.Invoke(); return Task.FromResult<IReadOnlyList<OutboxMessageLease>>([message]); }
        public async Task<bool> MarkPublishedAsync(Guid eventId, string owner, Guid token, CancellationToken cancellationToken) { Marked = MarkOperation is null ? MarkResult : await MarkOperation(cancellationToken); return Marked; }
        public Task<bool> RenewAsync(Guid eventId, string owner, Guid token, TimeSpan duration, CancellationToken cancellationToken) { BeforeRenew?.Invoke(); Renewed = true; return Task.FromResult(true); }
        public Task<OutboxMetrics> GetMetricsAsync(CancellationToken cancellationToken) => Task.FromResult(Metrics);
    }

    private sealed class RecordingSender : IOutboxMessageSender
    {
        public Exception? Failure { get; init; }
        public bool Cancel { get; init; }
        public bool WaitForCancellation { get; init; }
        public Action? BeforeSend { get; init; }
        public Guid MessageId { get; private set; }
        public string? Payload { get; private set; }
        public List<(Guid EventId, string Payload)> Sends { get; } = [];
        public TaskCompletionSource SendStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CancellationObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task SendAsync(OutboxMessageLease message, CancellationToken cancellationToken)
        { BeforeSend?.Invoke(); if (Failure is not null) throw Failure; if (Cancel) throw new OperationCanceledException(cancellationToken); if (WaitForCancellation) return WaitAsync(cancellationToken); MessageId = message.EventId; Payload = message.Payload; Sends.Add((message.EventId, message.Payload)); return Task.CompletedTask; }
        private async Task WaitAsync(CancellationToken token)
        {
            SendStarted.SetResult();
            using var registration = token.Register(CancellationObserved.SetResult);
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        }
    }

    private sealed class CrashReclaimStore(OutboxMessageLease persistedMessage, TimeProvider timeProvider) : IOutboxLeaseStore
    {
        private OutboxMessageLease? activeLease;
        private bool crashOnNextMark = true;
        public bool Marked { get; private set; }
        public bool ExpiredLeaseObserved { get; private set; }
        public List<Guid> LeaseTokens { get; } = [];

        public Task<IReadOnlyList<OutboxMessageLease>> ClaimAsync(string owner, int batchSize, TimeSpan duration, CancellationToken cancellationToken)
        {
            if (Marked || activeLease is not null && activeLease.LeaseExpiresAt > timeProvider.GetUtcNow())
                return Task.FromResult<IReadOnlyList<OutboxMessageLease>>([]);

            ExpiredLeaseObserved = activeLease is not null;
            activeLease = persistedMessage with
            {
                AttemptCount = (activeLease?.AttemptCount ?? 0) + 1,
                LeaseOwner = owner,
                LeaseToken = Guid.NewGuid(),
                LeaseExpiresAt = timeProvider.GetUtcNow() + duration
            };
            LeaseTokens.Add(activeLease.LeaseToken);
            return Task.FromResult<IReadOnlyList<OutboxMessageLease>>([activeLease]);
        }

        public Task<bool> MarkPublishedAsync(Guid eventId, string owner, Guid token, CancellationToken cancellationToken)
        {
            if (crashOnNextMark)
            {
                crashOnNextMark = false;
                throw new InvalidOperationException("process terminated after send");
            }

            Marked = activeLease is not null && activeLease.EventId == eventId && activeLease.LeaseOwner == owner && activeLease.LeaseToken == token;
            return Task.FromResult(Marked);
        }

        public Task<bool> RenewAsync(Guid eventId, string owner, Guid token, TimeSpan duration, CancellationToken cancellationToken)
        {
            if (activeLease is null || activeLease.EventId != eventId || activeLease.LeaseOwner != owner || activeLease.LeaseToken != token || activeLease.LeaseExpiresAt <= timeProvider.GetUtcNow())
                return Task.FromResult(false);

            activeLease = activeLease with { LeaseExpiresAt = timeProvider.GetUtcNow() + duration };
            return Task.FromResult(true);
        }

        public Task<OutboxMetrics> GetMetricsAsync(CancellationToken cancellationToken) => Task.FromResult(new OutboxMetrics(Marked ? 0 : 1, TimeSpan.FromMinutes(1)));
    }

    private sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private readonly Lock sync = new();
        private readonly List<ManualTimer> timers = [];
        private DateTimeOffset utcNow = start;

        public override DateTimeOffset GetUtcNow()
        {
            lock (sync) return utcNow;
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            lock (sync) timers.Add(timer);
            timer.Change(dueTime, period);
            return timer;
        }

        public void Advance(TimeSpan duration)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);
            List<(TimerCallback Callback, object? State)> due = [];
            lock (sync)
            {
                utcNow += duration;
                foreach (var timer in timers)
                    if (timer.TakeIfDue(utcNow) is { } callback)
                        due.Add(callback);
            }

            foreach (var callback in due)
                callback.Callback(callback.State);
        }

        private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
        {
            private DateTimeOffset? dueAt;
            private TimeSpan period;
            private bool disposed;

            public bool Change(TimeSpan dueTime, TimeSpan newPeriod)
            {
                lock (owner.sync)
                {
                    if (disposed) return false;
                    period = newPeriod;
                    dueAt = dueTime == Timeout.InfiniteTimeSpan ? null : owner.utcNow + dueTime;
                    return true;
                }
            }

            public void Dispose()
            {
                lock (owner.sync) disposed = true;
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }

            public (TimerCallback Callback, object? State)? TakeIfDue(DateTimeOffset now)
            {
                if (disposed || dueAt is null || dueAt > now) return null;
                dueAt = period == Timeout.InfiniteTimeSpan ? null : now + period;
                return (callback, state);
            }
        }
    }

    private sealed class TestTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow;
    }

}
