using CloudOrders.Application.Abstractions;
using CloudOrders.Contracts.Orders;

namespace CloudOrders.Application.Orders;

public sealed class GetOrderHandler(IOrderRepository orderRepository)
{
    public async Task<OwnedOrderResponse?> Handle(Guid orderId, CancellationToken cancellationToken)
    {
        var ownedOrder = await orderRepository.GetOwnedAsync(orderId, cancellationToken);
        return ownedOrder is null
            ? null
            : new OwnedOrderResponse(ownedOrder.Owner, OrderResponseMapper.ToResponse(ownedOrder.Order));
    }
}

public sealed record OwnedOrderResponse(OrderOwner Owner, OrderResponse Response);
