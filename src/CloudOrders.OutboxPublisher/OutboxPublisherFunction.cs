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
    public int Pending => Failed + StaleToken;
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
                try
                {
                    if (!await WithinDeadline(deadline, cancellationToken, token => leaseStore.RenewAsync(row.EventId, row.LeaseOwner, row.LeaseToken, LeaseDuration, token)))
                    { stale++; logger?.LogWarning("OutboxLeaseRenewFailed outcome=stale_token"); continue; }
                    await WithinDeadline(deadline, cancellationToken, token => sender.SendAsync(row, token));
                    if (await WithinDeadline(deadline, cancellationToken, token => leaseStore.MarkPublishedAsync(row.EventId, row.LeaseOwner, row.LeaseToken, token)))
                    { published++; logger?.LogInformation("OutboxPublished"); }
                    else
                    { stale++; logger?.LogWarning("OutboxMarkFailed outcome=stale_token"); }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                { failed++; deadlineReached = true; break; }
                catch (Exception exception)
                { failed++; logger?.LogWarning(exception, "OutboxPublishAttempt failed"); }
            }
            if (deadlineReached) break;
        }
        var result = new OutboxDrainResult(claimed, published, failed, stale, deadlineReached)
        { OldestPendingAge = failed + stale > 0 ? DrainDuration : TimeSpan.Zero };
        logger?.LogInformation("OutboxDrainCounters pending={Pending} oldestPendingAgeSeconds={OldestPendingAgeSeconds} publishSuccess={PublishSuccess} publishFailure={PublishFailure}", result.Counters.Pending, result.Counters.OldestPendingAge.TotalSeconds, result.Counters.PublishSuccess, result.Counters.PublishFailure);
        return result;
    }

    private async Task<T> WithinDeadline<T>(DateTimeOffset deadline, CancellationToken cancellationToken, Func<CancellationToken, Task<T>> operation)
    {
        var remaining = deadline - timeProvider.GetUtcNow();
        if (remaining <= TimeSpan.Zero) throw new OperationCanceledException();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(remaining);
        return await operation(timeout.Token);
    }

    private Task WithinDeadline(DateTimeOffset deadline, CancellationToken cancellationToken, Func<CancellationToken, Task> operation)
        => WithinDeadline(deadline, cancellationToken, async token => { await operation(token); return true; });
}
