using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CloudOrders.IntegrationTests;

public sealed class AuthorizationPolicyIntegrationTests : IClassFixture<PolicyWebApplicationFactory>
{
    private readonly PolicyWebApplicationFactory factory;

    public AuthorizationPolicyIntegrationTests(PolicyWebApplicationFactory factory) => this.factory = factory;

    [Theory]
    [InlineData("Orders.Read", null, HttpStatusCode.NotFound)]
    [InlineData("Orders.Read", "user.admin", HttpStatusCode.NotFound)]
    [InlineData("Orders.Read", "user.viewer", HttpStatusCode.Forbidden)]
    [InlineData("Orders.Read", "User.admin", HttpStatusCode.Forbidden)]
    [InlineData("Orders.Write", null, HttpStatusCode.Forbidden)]
    public async Task ReadPolicyRequiresExactScopeAndKnownRoles(string scope, string? role, HttpStatusCode expected)
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");
        client.DefaultRequestHeaders.Add("X-Test-Scopes", scope);
        if (role is not null) client.DefaultRequestHeaders.Add("X-Test-Roles", role);

        using var response = await client.GetAsync($"/api/v1/orders/{Guid.NewGuid()}");

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task HealthRemainsAnonymousWhileOrderRouteChallenges()
    {
        using var client = factory.CreateClient();

        using var health = await client.GetAsync("/health/live");
        using var order = await client.GetAsync($"/api/v1/orders/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, order.StatusCode);
        Assert.Contains("Bearer", order.Headers.WwwAuthenticate.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DevelopmentOpenApiDocumentRequiresAuthentication()
    {
        using var developmentFactory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ExternalIdentity:Authority", "https://example.ciamlogin.com/11111111-1111-1111-1111-111111111111/v2.0");
            builder.UseSetting("ExternalIdentity:ValidIssuer", SignedJwtFactory.Issuer);
            builder.UseSetting("ExternalIdentity:TenantId", SignedJwtFactory.TenantId);
            builder.UseSetting("ExternalIdentity:Audience", SignedJwtFactory.Audience);
            builder.UseSetting("ExternalIdentity:AllowedClientIds:0", SignedJwtFactory.ClientId);
        });
        using var client = developmentFactory.CreateClient();

        using var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("Bearer", response.Headers.WwwAuthenticate.ToString(), StringComparison.Ordinal);
    }
}
