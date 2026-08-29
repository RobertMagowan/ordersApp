using CloudOrders.Infrastructure.Persistence;

namespace CloudOrders.UnitTests;

public sealed class CustomerReferenceGeneratorTests
{
    [Fact]
    public void CreateReturnsCusPrefixAndThirtyTwoUppercaseHexCharacters()
    {
        var reference = new CustomerReferenceGenerator().Create();

        Assert.Matches("^CUS-[0-9A-F]{32}$", reference);
    }
}
