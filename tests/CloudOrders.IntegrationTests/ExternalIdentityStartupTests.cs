using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CloudOrders.IntegrationTests;

public sealed class ExternalIdentityStartupTests
{
    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    [InlineData("Production")]
    public void StartupFailsForMissingExternalIdentityConfiguration(string environment)
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("ExternalIdentity:Authority", "");
        });

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("ExternalIdentity", exception.ToString(), StringComparison.Ordinal);
    }
}
