using CloudOrders.Domain.Orders;

namespace CloudOrders.Infrastructure.Persistence;

internal static class OrderPersistenceMapper
{
    public static OrderEntity ToEntity(Order order, Guid? customerProfileId = null) =>
        new()
        {
            Id = order.Id,
            CustomerReference = order.CustomerReference,
            ProductSku = order.ProductSku,
            Quantity = order.Quantity,
            Status = order.Status,
            CreatedAt = order.CreatedAt,
            UpdatedAt = order.UpdatedAt,
            CustomerProfileId = customerProfileId
                ?? throw new InvalidOperationException("Order ownership is required for SQL persistence.")
        };

    public static Order ToDomain(OrderEntity entity)
    {
        var order = Order.Create(
            entity.Id,
            entity.CustomerReference,
            entity.ProductSku,
            entity.Quantity,
            entity.CreatedAt);
        if (entity.Status is OrderStatus.Processing)
        {
            order.AdvanceToProcessing(entity.UpdatedAt);
        }

        return order;
    }
}
