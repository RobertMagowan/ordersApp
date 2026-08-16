namespace CloudOrders.Contracts.Orders;

public sealed record OrderResponse(
    Guid Id,
    string CustomerReference,
    string ProductSku,
    int Quantity,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
