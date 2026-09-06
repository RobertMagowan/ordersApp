using System.Net.Http.Headers;
using CloudOrders.Application.Identity;
using CloudOrders.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace CloudOrders.IntegrationTests;

internal sealed record TestCustomer(Guid ProfileId, Guid ObjectId, string CustomerReference);

internal sealed class OrderSqlJwtBearerWebApplicationFactory(
    string connectionString,
    IdempotencyRaceObserver? raceObserver = null,
    IAuthorizationAuditSink? auditSink = null) : WebApplicationFactory<Program>
{
    private static readonly TestCustomer DefaultCustomer = new(
        Guid.Parse("55555555-5555-5555-5555-555555555555"),
        Guid.Parse("44444444-4444-4444-4444-444444444444"),
        "CUST-001");
    private readonly SignedJwtFactory tokens = new();

    internal HttpClient CreateAuthenticatedClient()
        => CreateAuthenticatedClient(DefaultCustomer);

    internal HttpClient CreateAuthenticatedClient(TestCustomer customer, string[]? roles = null)
    {
        SeedCustomerProfileAsync(customer).GetAwaiter().GetResult();
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            tokens.CreateToken(
                oid: customer.ObjectId.ToString("D"),
                scope: "Orders.Read Orders.Write",
                roles: roles));
        return client;
    }

    private async Task SeedCustomerProfileAsync(TestCustomer customer)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            IF NOT EXISTS (SELECT 1 FROM dbo.CustomerProfiles WHERE Issuer = @issuer AND ObjectId = @objectId)
            BEGIN
                INSERT INTO dbo.CustomerProfiles (Id, CustomerReference, Issuer, ObjectId, ContactEmail, CreatedAt, UpdatedAt)
                VALUES (@id, @customerReference, @issuer, @objectId, NULL, SYSUTCDATETIME(), SYSUTCDATETIME());
            END
            """;
        command.Parameters.Add(new SqlParameter("@id", customer.ProfileId));
        command.Parameters.Add(new SqlParameter("@customerReference", customer.CustomerReference));
        command.Parameters.Add(new SqlParameter("@issuer", SignedJwtFactory.Issuer));
        command.Parameters.Add(new SqlParameter("@objectId", customer.ObjectId));
        try
        {
            await command.ExecuteNonQueryAsync();
        }
        catch (SqlException exception) when (exception.Number == 208)
        {
            // Readiness tests intentionally use an unmigrated database.
        }
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

            if (auditSink is not null)
            {
                services.RemoveAll<IAuthorizationAuditSink>();
                services.AddSingleton(auditSink);
            }
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) tokens.Dispose();
        base.Dispose(disposing);
    }
}
