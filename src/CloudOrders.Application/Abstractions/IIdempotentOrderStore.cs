using CloudOrders.Application.Orders;
using CloudOrders.Contracts.Orders;
using CloudOrders.Domain.Orders;

namespace CloudOrders.Application.Abstractions;

public interface IIdempotentOrderStore
{
    Task<CreateOrderResult> CreateAsync(
        IdempotentOrderRequest request,
        CancellationToken cancellationToken);
}

public sealed record IdempotentOrderRequest(
    string SubjectId,
    Guid IdempotencyKey,
    byte[] RequestHash,
    Order Order,
    OrderCreatedIntegrationEventV1 IntegrationEvent,
    OrderResponse Response,
    string? TraceParent);
