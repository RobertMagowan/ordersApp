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
            builder.UseEnvironment(environment));

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("ExternalIdentity", exception.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ExternalIdentity:TenantId", "not-a-guid")]
    [InlineData("ExternalIdentity:Audience", "not-a-guid")]
    [InlineData("ExternalIdentity:AllowedClientIds:0", "not-a-guid")]
    [InlineData("ExternalIdentity:Authority", "http://example.ciamlogin.com/11111111-1111-1111-1111-111111111111/v2.0")]
    [InlineData("ExternalIdentity:Authority", " https://example.ciamlogin.com/11111111-1111-1111-1111-111111111111/v2.0")]
    [InlineData("ExternalIdentity:Authority", "https://example.ciamlogin.com/11111111-1111-1111-1111-111111111111/v2.0/")]
    [InlineData("ExternalIdentity:ValidIssuer", "http://11111111-1111-1111-1111-111111111111.ciamlogin.com/11111111-1111-1111-1111-111111111111/v2.0")]
    [InlineData("ExternalIdentity:ValidIssuer", "https://22222222-2222-2222-2222-222222222222.ciamlogin.com/22222222-2222-2222-2222-222222222222/v2.0")]
    public void StartupFailsForMalformedExternalIdentityConfiguration(string key, string value)
    {
        using var factory = CreateFactory("Production", key, value);

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("ExternalIdentity", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void StartupFailsForDuplicateAllowedClientIdsIgnoringGuidCase()
    {
        using var factory = CreateFactory("Production", "ExternalIdentity:AllowedClientIds:1", SignedJwtFactory.ClientId.ToUpperInvariant());

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("ExternalIdentity", exception.ToString(), StringComparison.Ordinal);
    }

    private static WebApplicationFactory<Program> CreateFactory(string environment, string key, string value) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("ExternalIdentity:Authority", "https://example.ciamlogin.com/11111111-1111-1111-1111-111111111111/v2.0");
            builder.UseSetting("ExternalIdentity:ValidIssuer", SignedJwtFactory.Issuer);
            builder.UseSetting("ExternalIdentity:TenantId", SignedJwtFactory.TenantId);
            builder.UseSetting("ExternalIdentity:Audience", SignedJwtFactory.Audience);
            builder.UseSetting("ExternalIdentity:AllowedClientIds:0", SignedJwtFactory.ClientId);
            builder.UseSetting(key, value);
        });
}
