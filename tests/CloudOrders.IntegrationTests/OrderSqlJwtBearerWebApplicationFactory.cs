using System.Net.Http.Headers;
using CloudOrders.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace CloudOrders.IntegrationTests;

internal sealed class OrderSqlJwtBearerWebApplicationFactory(
    string connectionString,
    IdempotencyRaceObserver? raceObserver = null) : WebApplicationFactory<Program>
{
    private readonly SignedJwtFactory tokens = new();

    internal HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            tokens.CreateToken(oid: Guid.NewGuid().ToString("D"), scope: "Orders.Read Orders.Write"));
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:CloudOrders", connectionString);
        builder.UseSetting("ExternalIdentity:Authority", "https://example.ciamlogin.com/11111111-1111-1111-1111-111111111111/v2.0");
        builder.UseSetting("ExternalIdentity:ValidIssuer", SignedJwtFactory.Issuer);
        builder.UseSetting("ExternalIdentity:TenantId", SignedJwtFactory.TenantId);
        builder.UseSetting("ExternalIdentity:Audience", SignedJwtFactory.Audience);
        builder.UseSetting("ExternalIdentity:AllowedClientIds:0", SignedJwtFactory.ClientId);
        builder.ConfigureTestServices(services =>
        {
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(new OpenIdConnectConfiguration
                {
                    Issuer = SignedJwtFactory.Issuer,
                    SigningKeys = { tokens.PublicKey }
                }));
            if (raceObserver is not null)
            {
                services.ConfigureDbContext<CloudOrdersDbContext>(options =>
                    options.AddInterceptors(raceObserver.CommandInterceptor, raceObserver.TransactionInterceptor));
            }
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) tokens.Dispose();
        base.Dispose(disposing);
    }
}
