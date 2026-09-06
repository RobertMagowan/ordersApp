using CloudOrders.Application.Identity;
using Microsoft.Extensions.Logging;

namespace CloudOrders.Api.Identity;

public sealed partial class LoggerAuthorizationAuditSink(
    ILogger<LoggerAuthorizationAuditSink> logger,
    string environment) : IAuthorizationAuditSink
{
    public ValueTask WriteAsync(AuthorizationAuditEvent auditEvent, CancellationToken cancellationToken)
    {
        LogAudit(
            logger,
            auditEvent.Action,
            auditEvent.Result,
            auditEvent.ActorCustomerProfileId,
            auditEvent.TargetCustomerProfileId,
            auditEvent.TargetOrderId,
            auditEvent.Capability,
            auditEvent.TraceId,
            environment);
        return ValueTask.CompletedTask;
    }

    [LoggerMessage(
        EventId = 4001,
        Level = LogLevel.Information,
        Message = "Authorization audit {Action} {Result} {ActorCustomerProfileId} {TargetCustomerProfileId} {TargetOrderId} {Capability} {TraceId} {Environment}")]
    private static partial void LogAudit(
        ILogger logger,
        AuthorizationAuditAction action,
        AuthorizationAuditResult result,
        Guid? actorCustomerProfileId,
        Guid? targetCustomerProfileId,
        Guid? targetOrderId,
        AuthorizationCapability capability,
        string traceId,
        string environment);
}
