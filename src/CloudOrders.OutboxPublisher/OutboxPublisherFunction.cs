using Azure.Messaging.ServiceBus;
using CloudOrders.Application.Abstractions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace CloudOrders.OutboxPublisher;

public interface IOutboxMessageSender
{
    Task SendAsync(OutboxMessageLease message, CancellationToken cancellationToken);
}

public sealed class ServiceBusOutboxMessageSender(ServiceBusSender sender) : IOutboxMessageSender
{
    public Task SendAsync(OutboxMessageLease message, CancellationToken cancellationToken)
    {
        var serviceBusMessage = new ServiceBusMessage(BinaryData.FromString(message.Payload))
        {
            MessageId = message.EventId.ToString("D"),
            Subject = message.MessageType,
            ContentType = "application/json"
        };
        serviceBusMessage.ApplicationProperties["messageType"] = message.MessageType;
        serviceBusMessage.ApplicationProperties["messageVersion"] = message.MessageVersion;
        if (message.TraceParent is not null)
            serviceBusMessage.ApplicationProperties["traceparent"] = message.TraceParent;
        return sender.SendMessageAsync(serviceBusMessage, cancellationToken);
    }
}

public sealed record OutboxDrainResult(int Claimed, int Published, int Failed, int StaleToken, bool DeadlineReached)
{
    public int Pending { get; init; }
    public TimeSpan OldestPendingAge { get; init; }
    public OutboxPublisherCounters Counters => new(Pending, OldestPendingAge, Published, Failed + StaleToken);
}

public sealed record OutboxPublisherCounters(int Pending, TimeSpan OldestPendingAge, int PublishSuccess, int PublishFailure);

public sealed class OutboxPublisherFunction(
    IOutboxLeaseStore leaseStore,
    IOutboxMessageSender sender,
    TimeProvider timeProvider,
    ILogger<OutboxPublisherFunction>? logger = null)
{
    public const int BatchSize = 500;
    public static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(90);
    public static readonly TimeSpan DrainDuration = TimeSpan.FromMinutes(8);
    private static readonly TimeSpan LeaseSafetyMargin = TimeSpan.FromSeconds(5);

    [Function("OutboxPublisher")]
    public Task RunAsync([TimerTrigger("*/10 * * * * *")] TimerInfo timer, CancellationToken cancellationToken) => DrainAsync(cancellationToken);

    public async Task<OutboxDrainResult> DrainAsync(CancellationToken cancellationToken)
    {
        var deadline = timeProvider.GetUtcNow() + DrainDuration;
        var owner = $"publisher-{Environment.MachineName}-{Guid.NewGuid():N}";
        var claimed = 0;
        var published = 0;
        var failed = 0;
        var stale = 0;
        var deadlineReached = false;
        while (!cancellationToken.IsCancellationRequested)
        {
            var remaining = deadline - timeProvider.GetUtcNow();
            if (remaining <= TimeSpan.Zero) { deadlineReached = true; break; }
            IReadOnlyList<OutboxMessageLease> rows;
            try { rows = await WithinDeadline(deadline, cancellationToken, token => leaseStore.ClaimAsync(owner, BatchSize, LeaseDuration, token)); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { deadlineReached = true; break; }
            if (rows.Count == 0) break;
            claimed += rows.Count;
            foreach (var row in rows)
            {
                remaining = deadline - timeProvider.GetUtcNow();
                if (remaining <= TimeSpan.Zero) { deadlineReached = true; break; }
                // Start before renewal: database UTC grants at least this duration from
                // the request start, without comparing the database and host clocks.
                // One timer covers renewal latency, send, and the conditional mark.
                var leaseDeadline = timeProvider.GetUtcNow() + LeaseDuration - LeaseSafetyMargin;
                var operationDeadline = leaseDeadline < deadline ? leaseDeadline : deadline;
                try
                {
                    var outcome = await WithinDeadline(operationDeadline, cancellationToken, async token =>
                    {
                        var renewed = await leaseStore.RenewAsync(row.EventId, row.LeaseOwner, row.LeaseToken, LeaseDuration, token);
                        token.ThrowIfCancellationRequested();
                        if (!renewed) return (Renewed: false, Published: false);
                        await sender.SendAsync(row, token);
                        // A sender may return normally after observing cancellation.
                        // Never begin the mark once the shared lease budget is spent.
                        token.ThrowIfCancellationRequested();
                        return (Renewed: true, Published: await leaseStore.MarkPublishedAsync(row.EventId, row.LeaseOwner, row.LeaseToken, token));
                    });
                    if (!outcome.Renewed)
                    { stale++; logger?.LogWarning("OutboxLeaseRenewFailed outcome=stale_token"); }
                    else if (outcome.Published)
                    { published++; logger?.LogInformation("OutboxPublished"); }
                    else
                    { stale++; logger?.LogWarning("OutboxMarkFailed outcome=stale_token"); }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    failed++;
                    if (operationDeadline == deadline || timeProvider.GetUtcNow() >= deadline)
                    { deadlineReached = true; break; }
                    logger?.LogWarning("OutboxPublishAttempt failed outcome=lease_deadline");
                }
                catch (Exception exception)
                { failed++; logger?.LogWarning(exception, "OutboxPublishAttempt failed"); }
            }
            if (deadlineReached) break;
        }
        OutboxMetrics metrics;
        try { metrics = await WithinDeadline(deadline, cancellationToken, leaseStore.GetMetricsAsync); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { deadlineReached = true; metrics = new(0, TimeSpan.Zero); }
        var result = new OutboxDrainResult(claimed, published, failed, stale, deadlineReached)
        { Pending = metrics.PendingCount, OldestPendingAge = metrics.OldestPendingAge };
        logger?.LogInformation("OutboxDrainCounters pending={Pending} oldestPendingAgeSeconds={OldestPendingAgeSeconds} publishSuccess={PublishSuccess} publishFailure={PublishFailure}", result.Counters.Pending, result.Counters.OldestPendingAge.TotalSeconds, result.Counters.PublishSuccess, result.Counters.PublishFailure);
        return result;
    }

    private async Task<T> WithinDeadline<T>(DateTimeOffset deadline, CancellationToken cancellationToken, Func<CancellationToken, Task<T>> operation)
    {
        var remaining = deadline - timeProvider.GetUtcNow();
        if (remaining <= TimeSpan.Zero) throw new OperationCanceledException();
        using var timer = new CancellationTokenSource(remaining, timeProvider);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timer.Token);
        timeout.Token.ThrowIfCancellationRequested();
        var result = await operation(timeout.Token);
        timeout.Token.ThrowIfCancellationRequested();
        return result;
    }
}
