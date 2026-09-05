namespace CloudOrders.Application.Identity;

public sealed record CustomerProfile(
    Guid Id,
    string CustomerReference,
    string Issuer,
    Guid ObjectId,
    string? ContactEmail);
