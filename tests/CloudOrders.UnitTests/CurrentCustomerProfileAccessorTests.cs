using System.Security.Claims;
using CloudOrders.Api.Identity;
using CloudOrders.Application.Identity;
using Microsoft.AspNetCore.Http;

namespace CloudOrders.UnitTests;

public sealed class CurrentCustomerProfileAccessorTests
{
    [Fact]
    public async Task ResolvesTheAuthenticatedExternalSubjectOnce()
    {
        var issuer = "https://example.ciamlogin.com/11111111-1111-1111-1111-111111111111/v2.0";
        var objectId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("iss", issuer), new Claim("oid", objectId.ToString("D"))], "test"));
        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var store = new RecordingStore();

        var profile = await new CurrentCustomerProfileAccessor(
            accessor,
            store,
            new NullAuditSink(),
            new TestHostEnvironment()).GetAsync(CancellationToken.None);

        Assert.Equal(store.Profile, profile);
        Assert.Equal(new AuthenticatedSubject(issuer, objectId, null), store.Subject);
    }

    private sealed class RecordingStore : ICustomerProfileStore
    {
        public CustomerProfile Profile { get; } = new(Guid.NewGuid(), "CUST-001", "issuer", Guid.NewGuid(), null);
        public AuthenticatedSubject? Subject { get; private set; }
        public Task<CustomerProfile> GetOrCreateAsync(AuthenticatedSubject subject, CancellationToken cancellationToken)
        {
            Subject = subject;
            return Task.FromResult(Profile);
        }
        public Task<CustomerProfile?> FindByReferenceAsync(string customerReference, CancellationToken cancellationToken) => Task.FromResult<CustomerProfile?>(null);
    }

    private sealed class NullAuditSink : IAuthorizationAuditSink
    {
        public ValueTask WriteAsync(AuthorizationAuditEvent auditEvent, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class TestHostEnvironment : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "CloudOrders.UnitTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
