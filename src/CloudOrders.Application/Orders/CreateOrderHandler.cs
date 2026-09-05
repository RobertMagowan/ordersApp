using CloudOrders.Application.Abstractions;
using CloudOrders.Contracts.Orders;
using CloudOrders.Domain.Orders;

namespace CloudOrders.Application.Orders;

public sealed class CreateOrderHandler(
    IIdempotentOrderStore idempotentOrderStore,
    TimeProvider timeProvider)
{
    private const string LegacySubjectId = "local-development-subject";

    public async Task<CreateOrderResult> Handle(
        CreateOrderCommand command,
        Guid idempotencyKey,
        string? traceParent,
        CancellationToken cancellationToken)
    {
        try
        {
            var createdAt = timeProvider.GetUtcNow();
            var order = Order.Create(
                Guid.NewGuid(),
                command.CustomerReference,
                command.ProductSku,
                command.Quantity,
                createdAt);

            var response = OrderResponseMapper.ToResponse(order);
            var integrationEvent = new OrderCreatedIntegrationEventV1(
                Guid.NewGuid(),
                order.Id,
                order.CustomerReference,
                order.ProductSku,
                order.Quantity,
                order.CreatedAt);
            var request = new IdempotentOrderRequest(
                LegacySubjectId,
                idempotencyKey,
                IdempotencyRequestHasher.Compute(LegacySubjectId, order),
                order,
                integrationEvent,
                response,
                traceParent);

            return await idempotentOrderStore.CreateAsync(request, cancellationToken);
        }
        catch (DomainValidationException exception)
        {
            return CreateOrderResult.ValidationError(exception.Message);
        }
    }

}
