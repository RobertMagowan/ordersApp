using System.Net;

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
}
