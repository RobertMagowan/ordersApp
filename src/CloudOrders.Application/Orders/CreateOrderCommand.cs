namespace CloudOrders.Application.Orders;

public sealed record CreateOrderCommand(
    string CustomerReference,
    string ProductSku,
    int Quantity);
