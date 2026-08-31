namespace CloudOrders.Application.Identity;

public interface ICustomerProfileStore
{
    Task<CustomerProfile> GetOrCreateAsync(AuthenticatedSubject subject, CancellationToken cancellationToken);

    Task<CustomerProfile?> FindByReferenceAsync(string customerReference, CancellationToken cancellationToken);
}
