using CloudOrders.Domain.Orders;

namespace CloudOrders.Infrastructure.Persistence;

internal sealed class OrderEntity
{
    public Guid Id { get; set; }

    public required string CustomerReference { get; set; }

    public required string ProductSku { get; set; }

    public int Quantity { get; set; }

    public OrderStatus Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public Guid? CustomerProfileId { get; set; }

    public byte[] RowVersion { get; set; } = [];
}
