using CloudOrders.Application.Abstractions;
using CloudOrders.Domain.Orders;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CloudOrders.IntegrationTests;

public sealed class PolicyWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ExternalIdentity:Authority", "https://example.ciamlogin.com/11111111-1111-1111-1111-111111111111/v2.0");
        builder.UseSetting("ExternalIdentity:ValidIssuer", SignedJwtFactory.Issuer);
        builder.UseSetting("ExternalIdentity:TenantId", SignedJwtFactory.TenantId);
        builder.UseSetting("ExternalIdentity:Audience", SignedJwtFactory.Audience);
        builder.UseSetting("ExternalIdentity:AllowedClientIds:0", SignedJwtFactory.ClientId);
        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(PolicyTestAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, PolicyTestAuthenticationHandler>(PolicyTestAuthenticationHandler.SchemeName, _ => { });
            services.RemoveAll<IOrderRepository>();
            services.AddScoped<IOrderRepository, NullOrderRepository>();
        });
    }

    private sealed class NullOrderRepository : IOrderRepository
    {
        public Task AddAsync(Order order, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<Order?> GetAsync(Guid orderId, CancellationToken cancellationToken) => Task.FromResult<Order?>(null);
    }
}
