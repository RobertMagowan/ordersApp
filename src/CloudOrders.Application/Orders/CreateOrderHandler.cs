using CloudOrders.Application.Abstractions;
using CloudOrders.Contracts.Orders;
using CloudOrders.Domain.Orders;

namespace CloudOrders.Application.Orders;

public sealed class CreateOrderHandler(
    IOrderRepository orderRepository,
    IOutboxWriter outboxWriter,
    TimeProvider timeProvider)
{
    public async Task<ApplicationResult<OrderResponse>> Handle(
        CreateOrderCommand command,
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

            await orderRepository.AddAsync(order, cancellationToken);
            await outboxWriter.AddAsync(
                new OrderCreatedIntegrationEventV1(
                    Guid.NewGuid(),
                    order.Id,
                    order.CustomerReference,
                    order.ProductSku,
                    order.Quantity,
                    order.CreatedAt),
                cancellationToken);

            return new ApplicationResult<OrderResponse>(true, OrderResponseMapper.ToResponse(order), null, null);
        }
        catch (DomainValidationException exception)
        {
            return new ApplicationResult<OrderResponse>(false, null, "validation_error", exception.Message);
        }
    }

}
