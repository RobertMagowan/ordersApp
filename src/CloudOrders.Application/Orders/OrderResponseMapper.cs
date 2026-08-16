using CloudOrders.Contracts.Orders;
using CloudOrders.Domain.Orders;

namespace CloudOrders.Application.Orders;

internal static class OrderResponseMapper
{
    public static OrderResponse ToResponse(Order order) =>
        new(
            order.Id,
            order.CustomerReference,
            order.ProductSku,
            order.Quantity,
            order.Status switch
            {
                OrderStatus.Pending => "pending",
                OrderStatus.Processing => "processing",
                _ => throw new InvalidOperationException("Unknown order status.")
            },
            order.CreatedAt,
            order.UpdatedAt);
}
