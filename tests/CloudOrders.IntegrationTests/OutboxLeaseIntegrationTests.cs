using CloudOrders.Application.Abstractions;
using CloudOrders.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using System.Text.Json;

namespace CloudOrders.IntegrationTests;

[Collection(SqlServerTestGroup.Name)]
public sealed class OutboxLeaseIntegrationTests(SqlServerFixture sqlServer)
{
    [Fact]
    public async Task ConcurrentClaimersAtomicallyClaimRowsAndRejectStaleToken()
    {
        await using var database = await sqlServer.CreateDatabaseAsync();
        var options = new DbContextOptionsBuilder<CloudOrdersDbContext>().UseSqlServer(database.ConnectionString).Options;
        var factory = new TestContextFactory(options);
        var store = new SqlIdempotentOrderStore(factory, TimeProvider.System);
        var eventId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        await using (var connection = new SqlConnection(database.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO dbo.CustomerProfiles (Id, CustomerReference, Issuer, ObjectId, CreatedAt, UpdatedAt) VALUES (@p, 'C', 'issuer', @o, SYSUTCDATETIME(), SYSUTCDATETIME()); INSERT INTO dbo.Orders (Id, CustomerReference, ProductSku, Quantity, Status, CreatedAt, UpdatedAt, CustomerProfileId) VALUES (@id, 'C', 'SKU', 1, 'Pending', SYSUTCDATETIME(), SYSUTCDATETIME(), @p); INSERT INTO dbo.OutboxMessages (EventId, OrderId, AggregateId, MessageType, MessageVersion, Payload, OccurredAt, CreatedAt, AttemptCount) VALUES (@e, @id, @id, 'orders.order-created', 1, '{}', SYSUTCDATETIME(), SYSUTCDATETIME(), 0);";
            command.Parameters.AddWithValue("@p", Guid.NewGuid()); command.Parameters.AddWithValue("@o", Guid.NewGuid()); command.Parameters.AddWithValue("@id", orderId); command.Parameters.AddWithValue("@e", eventId);
            await command.ExecuteNonQueryAsync();
        }
        var first = store.ClaimAsync("publisher-a", 500, TimeSpan.FromSeconds(90), CancellationToken.None);
        var second = store.ClaimAsync("publisher-b", 500, TimeSpan.FromSeconds(90), CancellationToken.None);
        var claims = await Task.WhenAll(first, second);

        Assert.Equal(1, claims.Sum(batch => batch.Count));
        var claimed = claims.SelectMany(batch => batch).Single();
        Assert.False(await store.MarkPublishedAsync(claimed.EventId, "publisher-b", claimed.LeaseToken, CancellationToken.None));
        Assert.True(await store.MarkPublishedAsync(claimed.EventId, claimed.LeaseOwner, claimed.LeaseToken, CancellationToken.None));
    }

    [Fact]
    public async Task ClaimCapsAtomicBatchAtFiveHundredRows()
    {
        await using var database = await sqlServer.CreateDatabaseAsync();
        await SeedOutboxAsync(database.ConnectionString, 501);
        var store = CreateStore(database.ConnectionString);

        var claims = await Task.WhenAll(
            store.ClaimAsync("publisher-cap-a", 500, TimeSpan.FromSeconds(90), CancellationToken.None),
            store.ClaimAsync("publisher-cap-b", 500, TimeSpan.FromSeconds(90), CancellationToken.None));

        Assert.Equal(501, claims.Sum(x => x.Count));
        Assert.All(claims, batch => Assert.InRange(batch.Count, 1, 500));
    }

    [Fact]
    public async Task ConcurrentClaimsWorkWhenReadCommittedSnapshotIsEnabled()
    {
        await using var database = await sqlServer.CreateDatabaseAsync();
        await ExecuteAsync(database.ConnectionString, "ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE", _ => { });
        Assert.Equal(1, await ScalarAsync<int>(database.ConnectionString, "SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = DB_NAME()"));
        await SeedOutboxAsync(database.ConnectionString, 501);
        var store = CreateStore(database.ConnectionString);

        var claims = await Task.WhenAll(
            store.ClaimAsync("publisher-rcsi-a", 500, TimeSpan.FromSeconds(90), CancellationToken.None),
            store.ClaimAsync("publisher-rcsi-b", 500, TimeSpan.FromSeconds(90), CancellationToken.None));

        Assert.Equal(501, claims.Sum(x => x.Count));
        Assert.All(claims, batch => Assert.InRange(batch.Count, 1, 500));
    }

    [Fact]
    public async Task ExpiredLeaseCanBeReclaimedWithNewToken()
    {
        await using var database = await sqlServer.CreateDatabaseAsync();
        await SeedOutboxAsync(database.ConnectionString, 1);
        var store = CreateStore(database.ConnectionString);

        var first = Assert.Single(await store.ClaimAsync("publisher-old", 500, TimeSpan.FromMilliseconds(1), CancellationToken.None));
        await WaitForAsync(async () =>
            await ScalarAsync<int>(database.ConnectionString, "SELECT COUNT(*) FROM dbo.OutboxMessages WHERE LeaseExpiresAt <= SYSUTCDATETIME()") == 1);
        var reclaimed = Assert.Single(await store.ClaimAsync("publisher-new", 500, TimeSpan.FromSeconds(90), CancellationToken.None));

        Assert.NotEqual(first.LeaseToken, reclaimed.LeaseToken);
        Assert.False(await store.MarkPublishedAsync(first.EventId, first.LeaseOwner, first.LeaseToken, CancellationToken.None));
        Assert.True(await store.MarkPublishedAsync(reclaimed.EventId, reclaimed.LeaseOwner, reclaimed.LeaseToken, CancellationToken.None));
    }

    [Fact]
    public async Task RenewRequiresMatchingActiveLeaseAndToken()
    {
        await using var database = await sqlServer.CreateDatabaseAsync();
        await SeedOutboxAsync(database.ConnectionString, 1);
        var store = CreateStore(database.ConnectionString);
        var claim = Assert.Single(await store.ClaimAsync("publisher-renew", 1, TimeSpan.FromSeconds(90), CancellationToken.None));

        Assert.True(await store.RenewAsync(claim.EventId, claim.LeaseOwner, claim.LeaseToken, TimeSpan.FromSeconds(90), CancellationToken.None));
        Assert.False(await store.RenewAsync(claim.EventId, "another-owner", claim.LeaseToken, TimeSpan.FromSeconds(90), CancellationToken.None));
        Assert.False(await store.RenewAsync(claim.EventId, claim.LeaseOwner, Guid.NewGuid(), TimeSpan.FromSeconds(90), CancellationToken.None));
        await ExecuteAsync(database.ConnectionString, "UPDATE dbo.OutboxMessages SET LeaseExpiresAt = DATEADD(second, -1, SYSUTCDATETIME()) WHERE EventId = @eventId", command => command.Parameters.AddWithValue("@eventId", claim.EventId));
        Assert.False(await store.RenewAsync(claim.EventId, claim.LeaseOwner, claim.LeaseToken, TimeSpan.FromSeconds(90), CancellationToken.None));
    }

    [Fact]
    public async Task ClaimPreservesCanonicalEventIdentityAndVersionedMetadata()
    {
        await using var database = await sqlServer.CreateDatabaseAsync();
        await SeedOutboxAsync(database.ConnectionString, 1);
        var store = CreateStore(database.ConnectionString);

        var claim = Assert.Single(await store.ClaimAsync("publisher-contract", 500, TimeSpan.FromSeconds(90), CancellationToken.None));
        using var payload = JsonDocument.Parse(claim.Payload);

        Assert.Equal(claim.EventId, payload.RootElement.GetProperty("eventId").GetGuid());
        Assert.Equal(claim.OrderId, payload.RootElement.GetProperty("orderId").GetGuid());
        Assert.Equal(claim.MessageVersion, payload.RootElement.GetProperty("messageVersion").GetInt32());
        Assert.Equal(claim.MessageType, payload.RootElement.GetProperty("messageType").GetString());
    }

    private static SqlIdempotentOrderStore CreateStore(string connectionString)
    {
        var options = new DbContextOptionsBuilder<CloudOrdersDbContext>().UseSqlServer(connectionString).Options;
        return new SqlIdempotentOrderStore(new TestContextFactory(options), TimeProvider.System);
    }

    private static async Task SeedOutboxAsync(string connectionString, int count)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        var profile = Guid.NewGuid();
        await using var profileCommand = connection.CreateCommand();
        profileCommand.CommandText = "INSERT INTO dbo.CustomerProfiles (Id, CustomerReference, Issuer, ObjectId, CreatedAt, UpdatedAt) VALUES (@profile, 'PROFILE', 'issuer', @objectId, SYSUTCDATETIME(), SYSUTCDATETIME());";
        profileCommand.Parameters.AddWithValue("@profile", profile);
        profileCommand.Parameters.AddWithValue("@objectId", Guid.NewGuid());
        await profileCommand.ExecuteNonQueryAsync();

        for (var i = 0; i < count; i++)
        {
            var order = Guid.NewGuid();
            var eventId = Guid.NewGuid();
            command.CommandText = "INSERT INTO dbo.Orders (Id, CustomerReference, ProductSku, Quantity, Status, CreatedAt, UpdatedAt, CustomerProfileId) VALUES (@order, @customer, 'SKU', 1, 'Pending', SYSUTCDATETIME(), SYSUTCDATETIME(), @profile); INSERT INTO dbo.OutboxMessages (EventId, OrderId, AggregateId, MessageType, MessageVersion, Payload, OccurredAt, CreatedAt, AttemptCount) VALUES (@event, @order, @order, 'orders.order-created', 1, @payload, SYSUTCDATETIME(), SYSUTCDATETIME(), 0);";
            command.Parameters.Clear();
            command.Parameters.AddWithValue("@order", order);
            command.Parameters.AddWithValue("@customer", $"C{i}");
            command.Parameters.AddWithValue("@profile", profile);
            command.Parameters.AddWithValue("@event", eventId);
            command.Parameters.AddWithValue("@payload", $"{{\"eventId\":\"{eventId}\",\"orderId\":\"{order}\",\"messageType\":\"orders.order-created\",\"messageVersion\":1}}");
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task WaitForAsync(Func<Task<bool>> condition)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (await condition()) return;
            await Task.Delay(100);
        }
        Assert.Fail("Condition was not met within the polling window.");
    }

    private static async Task<T> ScalarAsync<T>(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync();
        Assert.NotNull(value);
        return (T)Convert.ChangeType(value, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task ExecuteAsync(string connectionString, string sql, Action<SqlCommand> configure)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        configure(command);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class TestContextFactory(DbContextOptions<CloudOrdersDbContext> options) : IDbContextFactory<CloudOrdersDbContext>
    {
        public CloudOrdersDbContext CreateDbContext() => new(options);
        public Task<CloudOrdersDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult<CloudOrdersDbContext>(new(options));
    }
}
