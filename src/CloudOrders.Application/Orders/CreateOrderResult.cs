using CloudOrders.Contracts.Orders;

namespace CloudOrders.Application.Orders;

public enum CreateOrderResultKind
{
    Created,
    Replayed,
    Conflict,
    ValidationError
}

public sealed record CreateOrderResult(
    CreateOrderResultKind Kind,
    OrderResponse? Response,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static CreateOrderResult Created(OrderResponse response) =>
        new(CreateOrderResultKind.Created, response, null, null);

    public static CreateOrderResult Replayed(OrderResponse response) =>
        new(CreateOrderResultKind.Replayed, response, null, null);

    public static CreateOrderResult Conflict() =>
        new(CreateOrderResultKind.Conflict, null, "idempotency_conflict", "The idempotency key was already used for another order request.");

    public static CreateOrderResult ValidationError(string message) =>
        new(CreateOrderResultKind.ValidationError, null, "validation_error", message);
}
