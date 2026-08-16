namespace CloudOrders.Domain.Orders;

public sealed class Order
{
    private Order(
        Guid id,
        string customerReference,
        string productSku,
        int quantity,
        DateTimeOffset createdAt)
    {
        Id = id;
        CustomerReference = customerReference;
        ProductSku = productSku;
        Quantity = quantity;
        Status = OrderStatus.Pending;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid Id { get; }

    public string CustomerReference { get; }

    public string ProductSku { get; }

    public int Quantity { get; }

    public OrderStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Order Create(
        Guid id,
        string customerReference,
        string productSku,
        int quantity,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new DomainValidationException("Order ID is required.");
        }

        var canonicalCustomerReference = Canonicalize(customerReference, allowDot: false, "customer reference");
        var canonicalProductSku = Canonicalize(productSku, allowDot: true, "product SKU");

        if (quantity is < 1 or > 100)
        {
            throw new DomainValidationException("Quantity must be between 1 and 100.");
        }

        var utcCreatedAt = createdAt.ToUniversalTime();
        return new Order(id, canonicalCustomerReference, canonicalProductSku, quantity, utcCreatedAt);
    }

    public void AdvanceToProcessing(DateTimeOffset updatedAt)
    {
        if (Status is not OrderStatus.Pending)
        {
            throw new DomainValidationException("Only pending orders can advance to processing.");
        }

        Status = OrderStatus.Processing;
        UpdatedAt = updatedAt.ToUniversalTime();
    }

    private static string Canonicalize(string value, bool allowDot, string fieldName)
    {
        var canonical = value.Trim().ToUpperInvariant();
        if (canonical.Length is < 1 or > 64)
        {
            throw new DomainValidationException($"{fieldName} must contain between 1 and 64 characters.");
        }

        foreach (var character in canonical)
        {
            var allowed = char.IsAsciiLetterOrDigit(character) || character is '-' or '_' || allowDot && character == '.';
            if (!allowed)
            {
                throw new DomainValidationException($"{fieldName} contains an invalid character.");
            }
        }

        return canonical;
    }
}
