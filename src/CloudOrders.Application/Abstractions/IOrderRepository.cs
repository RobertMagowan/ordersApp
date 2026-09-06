using CloudOrders.Domain.Orders;

namespace CloudOrders.Application.Abstractions;

public sealed record OrderOwner(Guid CustomerProfileId, string CustomerReference);

public sealed record OwnedOrder(Order Order, OrderOwner Owner);

public interface IOrderRepository
{
    Task AddAsync(Order order, CancellationToken cancellationToken);

    Task<OwnedOrder?> GetOwnedAsync(Guid orderId, CancellationToken cancellationToken);
}
