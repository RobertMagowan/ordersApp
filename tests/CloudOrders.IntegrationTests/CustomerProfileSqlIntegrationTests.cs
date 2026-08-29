using System.Security.Claims;
using CloudOrders.Api.Identity;
using CloudOrders.Application.Identity;
using CloudOrders.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CloudOrders.IntegrationTests;

[Collection(SqlServerTestGroup.Name)]
public sealed class CustomerProfileSqlIntegrationTests(SqlServerFixture sqlServer)
{
    [Fact]
    public async Task ConcurrentFirstAccessForTheSameIssuerAndObjectIdCreatesOneProfile()
    {
        await using var database = await sqlServer.CreateDatabaseAsync();
        var options = new DbContextOptionsBuilder<CloudOrdersDbContext>()
            .UseSqlServer(database.ConnectionString)
            .Options;
        var factory = new TestDbContextFactory(options);
        var profiles = new SqlCustomerProfileStore(factory, new BarrierReferenceGenerator(2), TimeProvider.System);
        var subject = new AuthenticatedSubject("https://issuer.example/v2.0", Guid.NewGuid(), "customer@example.test");

        var resolved = await Task.WhenAll(
            profiles.GetOrCreateAsync(subject, CancellationToken.None),
            profiles.GetOrCreateAsync(subject, CancellationToken.None));

        Assert.Equal(resolved[0], resolved[1]);
        Assert.Equal(1, await CountProfilesAsync(database.ConnectionString));
    }

    [Fact]
    public async Task SameVerifiedEmailWithDifferentObjectIdsCreatesSeparateProfiles()
    {
        await using var database = await sqlServer.CreateDatabaseAsync();
        var profiles = CreateStore(database.ConnectionString, new SequenceReferenceGenerator("CUS-00000000000000000000000000000001", "CUS-00000000000000000000000000000002"));

        var first = await profiles.GetOrCreateAsync(
            new AuthenticatedSubject("https://issuer.example/v2.0", Guid.NewGuid(), "customer@example.test"), CancellationToken.None);
        var second = await profiles.GetOrCreateAsync(
            new AuthenticatedSubject("https://issuer.example/v2.0", Guid.NewGuid(), "customer@example.test"), CancellationToken.None);

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(2, await CountProfilesAsync(database.ConnectionString));
    }

    [Fact]
    public async Task CustomerReferenceCollisionsRetryUntilAUniqueReferenceIsGenerated()
    {
        await using var database = await sqlServer.CreateDatabaseAsync();
        var generator = new SequenceReferenceGenerator(
            "CUS-00000000000000000000000000000004",
            "CUS-00000000000000000000000000000004",
            "CUS-00000000000000000000000000000004",
            "CUS-00000000000000000000000000000005");
        var profiles = CreateStore(database.ConnectionString, generator);

        await profiles.GetOrCreateAsync(
            new AuthenticatedSubject("https://issuer.example/v2.0", Guid.NewGuid(), null), CancellationToken.None);
        var resolved = await profiles.GetOrCreateAsync(
            new AuthenticatedSubject("https://issuer.example/v2.0", Guid.NewGuid(), null), CancellationToken.None);

        Assert.Equal("CUS-00000000000000000000000000000005", resolved.CustomerReference);
    }

    [Fact]
    public async Task UnverifiedOrMissingEmailIsStoredAsNullAndExistingContactNeverChanges()
    {
        await using var database = await sqlServer.CreateDatabaseAsync();
        var profiles = CreateStore(database.ConnectionString, new SequenceReferenceGenerator("CUS-00000000000000000000000000000003"));
        var subject = new AuthenticatedSubject("https://issuer.example/v2.0", Guid.NewGuid(), null);

        var created = await profiles.GetOrCreateAsync(subject, CancellationToken.None);
        var resolved = await profiles.GetOrCreateAsync(subject with { VerifiedContactEmail = "new@example.test" }, CancellationToken.None);

        Assert.Null(created.ContactEmail);
        Assert.Null(resolved.ContactEmail);
    }

    [Fact]
    public async Task EmailVerifiedFalseFlowsThroughSubjectReaderAndStoresNoContactEmail()
    {
        await using var database = await sqlServer.CreateDatabaseAsync();
        var profiles = CreateStore(database.ConnectionString, new SequenceReferenceGenerator("CUS-00000000000000000000000000000006"));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("iss", "https://issuer.example/v2.0"),
            new Claim("oid", Guid.NewGuid().ToString("D")),
            new Claim("email", "customer@example.test"),
            new Claim("email_verified", "false")
        ], "Bearer"));

        Assert.True(AuthenticatedSubjectReader.TryRead(principal, out var subject));

        var profile = await profiles.GetOrCreateAsync(subject, CancellationToken.None);

        Assert.Null(profile.ContactEmail);
    }

    private static SqlCustomerProfileStore CreateStore(string connectionString, ICustomerReferenceGenerator generator)
    {
        var options = new DbContextOptionsBuilder<CloudOrdersDbContext>().UseSqlServer(connectionString).Options;
        return new SqlCustomerProfileStore(new TestDbContextFactory(options), generator, TimeProvider.System);
    }

    private static async Task<int> CountProfilesAsync(string connectionString)
    {
        await using var context = new CloudOrdersDbContext(
            new DbContextOptionsBuilder<CloudOrdersDbContext>().UseSqlServer(connectionString).Options);
        return await context.Database.SqlQuery<int>($"SELECT COUNT(*) AS Value FROM dbo.CustomerProfiles").SingleAsync();
    }

    private sealed class TestDbContextFactory(DbContextOptions<CloudOrdersDbContext> options) : IDbContextFactory<CloudOrdersDbContext>
    {
        public CloudOrdersDbContext CreateDbContext() => new(options);

        public Task<CloudOrdersDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    private sealed class SequenceReferenceGenerator(params string[] references) : ICustomerReferenceGenerator
    {
        private readonly Queue<string> values = new(references);

        public string Create() => values.Dequeue();
    }

    private sealed class BarrierReferenceGenerator(int participants) : ICustomerReferenceGenerator, IDisposable
    {
        private readonly Barrier barrier = new(participants);
        private int sequence;

        public string Create()
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(30));
            return $"CUS-{Interlocked.Increment(ref sequence):X32}";
        }

        public void Dispose() => barrier.Dispose();
    }
}
