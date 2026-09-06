using CloudOrders.Application.Identity;

namespace CloudOrders.Api.Identity;

public sealed class CurrentCustomerProfileAccessor(
    IHttpContextAccessor httpContextAccessor,
    ICustomerProfileStore customerProfileStore,
    IAuthorizationAuditSink auditSink,
    IHostEnvironment hostEnvironment)
{
    public async Task<CustomerProfile> GetAsync(CancellationToken cancellationToken)
    {
        var principal = httpContextAccessor.HttpContext?.User
            ?? throw new InvalidOperationException("No HTTP request is available.");
        if (!AuthenticatedSubjectReader.TryRead(principal, out var subject))
        {
            throw new InvalidOperationException("The authenticated request has no valid subject.");
        }

        var profile = await customerProfileStore.GetOrCreateAsync(subject, cancellationToken);
        var traceId = System.Diagnostics.Activity.Current?.Id ?? httpContextAccessor.HttpContext!.TraceIdentifier;
        await auditSink.WriteAsync(
            new AuthorizationAuditEvent(
                AuthorizationAuditAction.ResolveProfile,
                AuthorizationAuditResult.Allowed,
                profile.Id,
                profile.Id,
                null,
                AuthorizationCapability.None,
                traceId,
                hostEnvironment.EnvironmentName),
            cancellationToken);
        return profile;
    }
}
