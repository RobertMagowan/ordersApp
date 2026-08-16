namespace CloudOrders.Application.Orders;

public sealed record ApplicationResult<T>(bool IsSuccess, T? Value, string? ErrorCode, string? ErrorMessage);
