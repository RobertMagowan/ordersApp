using System.Globalization;
using CloudOrders.Domain.Orders;

namespace CloudOrders.UnitTests;

public sealed class OrderTests
{
    [Fact]
    public void CreateNormalizesIdentifiersAndStartsPending()
    {
        var order = Order.Create(
            Guid.NewGuid(),
            "  cust-001 ",
            " sku-001 ",
            quantity: 2,
            DateTimeOffset.Parse("2026-08-16T14:30:22Z", CultureInfo.InvariantCulture));

        Assert.Equal("CUST-001", order.CustomerReference);
        Assert.Equal("SKU-001", order.ProductSku);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(2, order.Quantity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void CreateRejectsQuantityOutsideBusinessRange(int quantity)
    {
        var action = () => Order.Create(
            Guid.NewGuid(),
            "CUST-001",
            "SKU-001",
            quantity,
            DateTimeOffset.UtcNow);

        Assert.Throws<DomainValidationException>(action);
    }

    [Fact]
    public void AdvanceToProcessingAllowsPendingOrderOnly()
    {
        var order = Order.Create(Guid.NewGuid(), "CUST-001", "SKU-001", 1, DateTimeOffset.UtcNow);

        order.AdvanceToProcessing(DateTimeOffset.UtcNow.AddSeconds(1));

        Assert.Equal(OrderStatus.Processing, order.Status);
    }
}
