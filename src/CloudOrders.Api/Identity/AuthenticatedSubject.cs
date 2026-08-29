namespace CloudOrders.Api.Identity;

public sealed record AuthenticatedSubject(string Issuer, Guid ObjectId, string? VerifiedContactEmail);
