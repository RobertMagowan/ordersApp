using CloudOrders.Api.Identity;
using CloudOrders.Application.Identity;
using Microsoft.Extensions.Logging;

namespace CloudOrders.UnitTests;

public sealed class LoggerAuthorizationAuditSinkTests
{
    [Fact]
    public async Task WritesOnlyAllowlistedAuthorizationAuditFields()
    {
        var logger = new CapturingLogger<LoggerAuthorizationAuditSink>();
        var sink = new LoggerAuthorizationAuditSink(logger, "Testing");
        var auditEvent = new AuthorizationAuditEvent(
            AuthorizationAuditAction.CreateOrder,
            AuthorizationAuditResult.Allowed,
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            AuthorizationCapability.OrdersWrite,
            "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
            "Testing");

        await sink.WriteAsync(auditEvent, CancellationToken.None);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal(
            [
                "Action",
                "Result",
                "ActorCustomerProfileId",
                "TargetCustomerProfileId",
                "TargetOrderId",
                "Capability",
                "TraceId",
                "Environment"
            ],
            entry.State.Where(item => item.Key != "{OriginalFormat}").Select(item => item.Key));

        var serializedState = string.Join('|', entry.State.Select(item => item.Value));
        Assert.DoesNotContain("customer@example.com", serializedState, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CUST-PRIVATE", serializedState, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SKU-PRIVATE", serializedState, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bearer", serializedState, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<Entry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var fields = state is IEnumerable<KeyValuePair<string, object?>> values
                ? values.ToArray()
                : [new KeyValuePair<string, object?>("state", state)];
            Entries.Add(new Entry(logLevel, fields));
        }
    }

    private sealed record Entry(LogLevel Level, IReadOnlyList<KeyValuePair<string, object?>> State);
}
