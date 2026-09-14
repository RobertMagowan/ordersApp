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

public sealed record OutboxDrainResult(int Claimed, int Published, int Failed, int StaleToken, bool DeadlineReached);

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
            var rows = await leaseStore.ClaimAsync(owner, BatchSize, LeaseDuration, cancellationToken);
            if (rows.Count == 0) break;
            claimed += rows.Count;
            foreach (var row in rows)
            {
                remaining = deadline - timeProvider.GetUtcNow();
                if (remaining <= TimeSpan.Zero) { deadlineReached = true; break; }
                try
                {
                    using var sendCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    sendCancellation.CancelAfter(remaining);
                    await sender.SendAsync(row, sendCancellation.Token);
                    if (await leaseStore.MarkPublishedAsync(row.EventId, row.LeaseOwner, row.LeaseToken, cancellationToken))
                    { published++; logger?.LogInformation("OutboxPublished {EventId}", row.EventId); }
                    else
                    { stale++; logger?.LogWarning("OutboxMarkFailed {EventId} outcome=stale_token", row.EventId); }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                { failed++; deadlineReached = true; break; }
                catch (Exception exception)
                { failed++; logger?.LogWarning(exception, "OutboxPublishAttempt failed {EventId}", row.EventId); }
            }
            if (deadlineReached) break;
        }
        return new(claimed, published, failed, stale, deadlineReached);
    }
}
