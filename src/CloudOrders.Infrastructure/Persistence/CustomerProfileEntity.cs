namespace CloudOrders.Infrastructure.Persistence;

internal sealed class CustomerProfileEntity
{
    public Guid Id { get; set; }

    public required string CustomerReference { get; set; }

    public required string Issuer { get; set; }

    public Guid ObjectId { get; set; }

    public string? ContactEmail { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = [];
}
