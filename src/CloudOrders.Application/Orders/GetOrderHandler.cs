using CloudOrders.Application.Abstractions;
using CloudOrders.Contracts.Orders;

namespace CloudOrders.Application.Orders;

public sealed class GetOrderHandler(IOrderRepository orderRepository)
{
    public async Task<OrderResponse?> Handle(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await orderRepository.GetAsync(orderId, cancellationToken);
        return order is null ? null : OrderResponseMapper.ToResponse(order);
    }
}
