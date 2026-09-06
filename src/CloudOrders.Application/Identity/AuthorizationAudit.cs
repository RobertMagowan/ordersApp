namespace CloudOrders.Application.Identity;

public enum AuthorizationAuditAction
{
    Authenticate,
    ResolveProfile,
    GetCurrentCustomer,
    CreateOrder,
    GetOrder
}

public enum AuthorizationAuditResult
{
    Allowed,
    Denied,
    NotFound
}

public enum AuthorizationCapability
{
    None,
    OrdersRead,
    OrdersWrite,
    UserAdmin
}

public sealed record AuthorizationAuditEvent(
    AuthorizationAuditAction Action,
    AuthorizationAuditResult Result,
    Guid? ActorCustomerProfileId,
    Guid? TargetCustomerProfileId,
    Guid? TargetOrderId,
    AuthorizationCapability Capability,
    string TraceId,
    string Environment);

public interface IAuthorizationAuditSink
{
    ValueTask WriteAsync(AuthorizationAuditEvent auditEvent, CancellationToken cancellationToken);
}
