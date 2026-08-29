using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace CloudOrders.IntegrationTests;

internal sealed class PolicyTestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IConfiguration configuration)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    internal const string SchemeName = "PolicyTest";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var allowAll = string.Equals(configuration["PolicyTest:AllowAll"], "true", StringComparison.Ordinal);
        if ((!Request.Headers.TryGetValue("X-Test-Authenticated", out var authenticated) || authenticated != "true") && !allowAll)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim> { new("sub", "policy-test") };
        if (Request.Headers.TryGetValue("X-Test-Scopes", out var scopes)) claims.Add(new("scp", scopes.ToString()));
        else if (allowAll) claims.Add(new("scp", "Orders.Read Orders.Write"));
        var roleValues = Request.Headers["X-Test-Roles"].ToArray();
        foreach (var role in roleValues.SelectMany(value => (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries))) claims.Add(new("roles", role));
        var identity = new ClaimsIdentity(claims, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
