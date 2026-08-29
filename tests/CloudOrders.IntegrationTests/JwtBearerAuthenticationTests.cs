using System.Net;
using System.Net.Http.Headers;

namespace CloudOrders.IntegrationTests;

public sealed class JwtBearerAuthenticationTests : IDisposable
{
    private readonly SignedJwtFactory tokens = new();
    private readonly JwtBearerWebApplicationFactory factory;

    public JwtBearerAuthenticationTests() => factory = new JwtBearerWebApplicationFactory(tokens);

    [Fact]
    public async Task TrustedSignedTokenWithExactReadScopeReachesProtectedEndpoint()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.CreateToken(oid: Guid.NewGuid().ToString()));

        using var response = await client.GetAsync($"/api/v1/orders/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("bad")]
    public async Task MissingOrMalformedTokenChallenges(string? token)
    {
        using var client = factory.CreateClient();
        if (token is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync($"/api/v1/orders/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("Bearer", response.Headers.WwwAuthenticate.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TokenWithoutASignatureChallenges()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            tokens.CreateUnsignedToken(oid: Guid.NewGuid().ToString()));

        using var response = await client.GetAsync($"/api/v1/orders/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("invalid_token", response.Headers.WwwAuthenticate.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("not-before")]
    public async Task TokenOutsideItsValidLifetimeChallenges(string validity)
    {
        var now = DateTime.UtcNow;
        var token = validity == "expired"
            ? tokens.CreateToken(oid: Guid.NewGuid().ToString(), notBefore: now.AddMinutes(-10), expires: now.AddMinutes(-1))
            : tokens.CreateToken(oid: Guid.NewGuid().ToString(), notBefore: now.AddMinutes(1));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync($"/api/v1/orders/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("invalid_token", response.Headers.WwwAuthenticate.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("multiple")]
    public async Task TokenWithoutExactlyOneObjectIdChallenges(string oidShape)
    {
        var token = oidShape == "missing"
            ? tokens.CreateToken(oid: null)
            : tokens.CreateToken(objectIds: [Guid.NewGuid().ToString(), Guid.NewGuid().ToString()]);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync($"/api/v1/orders/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("invalid_token", response.Headers.WwwAuthenticate.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppOnlyTokenShapeChallenges()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.CreateAppOnlyToken());

        using var response = await client.GetAsync($"/api/v1/orders/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("invalid_token", response.Headers.WwwAuthenticate.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("issuer")]
    [InlineData("tenant")]
    [InlineData("audience")]
    [InlineData("client")]
    [InlineData("oid")]
    [InlineData("scope")]
    public async Task InvalidIdentityClaimsChallenge(string kind)
    {
        var token = kind switch
        {
            "issuer" => tokens.CreateToken(issuer: "https://wrong.example/v2.0", oid: Guid.NewGuid().ToString()),
            "tenant" => tokens.CreateToken(tenantId: Guid.NewGuid().ToString(), oid: Guid.NewGuid().ToString()),
            "audience" => tokens.CreateToken(audience: Guid.NewGuid().ToString(), oid: Guid.NewGuid().ToString()),
            "client" => tokens.CreateToken(clientId: Guid.NewGuid().ToString(), oid: Guid.NewGuid().ToString()),
            "oid" => tokens.CreateToken(oid: "not-a-guid"),
            _ => tokens.CreateToken(oid: Guid.NewGuid().ToString(), scope: null)
        };
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync($"/api/v1/orders/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("Bearer", response.Headers.WwwAuthenticate.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Orders.Write")]
    [InlineData("orders.read")]
    public async Task WrongScopeForReadIsForbidden(string scope)
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.CreateToken(oid: Guid.NewGuid().ToString(), scope: scope));

        using var response = await client.GetAsync($"/api/v1/orders/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    public void Dispose()
    {
        factory.Dispose();
        tokens.Dispose();
    }
}
