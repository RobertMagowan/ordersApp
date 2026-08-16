namespace CloudOrders.Domain.Orders;

public sealed class DomainValidationException(string message) : Exception(message);
