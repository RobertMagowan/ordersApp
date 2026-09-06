using CloudOrders.Application.Identity;

namespace CloudOrders.Api.Identity;

public sealed class CurrentCustomerProfileAccessor(
    IHttpContextAccessor httpContextAccessor,
    ICustomerProfileStore customerProfileStore)
{
    public Task<CustomerProfile> GetAsync(CancellationToken cancellationToken)
    {
        var principal = httpContextAccessor.HttpContext?.User
            ?? throw new InvalidOperationException("No HTTP request is available.");
        if (!AuthenticatedSubjectReader.TryRead(principal, out var subject))
        {
            throw new InvalidOperationException("The authenticated request has no valid subject.");
        }

        return customerProfileStore.GetOrCreateAsync(subject, cancellationToken);
    }
}
