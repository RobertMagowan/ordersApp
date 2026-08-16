namespace CloudOrders.Contracts.Orders;

public sealed record CreateOrderRequest(
    string CustomerReference,
    string ProductSku,
    int Quantity);
