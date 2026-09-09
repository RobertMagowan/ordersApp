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
            store.ClaimAsync("publisher-cap-b", 1, TimeSpan.FromSeconds(90), CancellationToken.None));

        Assert.Equal(501, claims.Sum(x => x.Count));
        Assert.Contains(claims, x => x.Count == 500);
        Assert.Contains(claims, x => x.Count == 1);
    }

    [Fact]
    public async Task ExpiredLeaseCanBeReclaimedWithNewToken()
    {
        await using var database = await sqlServer.CreateDatabaseAsync();
        await SeedOutboxAsync(database.ConnectionString, 1);
        var store = CreateStore(database.ConnectionString);

        var first = Assert.Single(await store.ClaimAsync("publisher-old", 500, TimeSpan.FromMilliseconds(1), CancellationToken.None));
        await Task.Delay(1200);
        var reclaimed = Assert.Single(await store.ClaimAsync("publisher-new", 500, TimeSpan.FromSeconds(90), CancellationToken.None));

        Assert.NotEqual(first.LeaseToken, reclaimed.LeaseToken);
        Assert.False(await store.MarkPublishedAsync(first.EventId, first.LeaseOwner, first.LeaseToken, CancellationToken.None));
        Assert.True(await store.MarkPublishedAsync(reclaimed.EventId, reclaimed.LeaseOwner, reclaimed.LeaseToken, CancellationToken.None));
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
        var values = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            var order = Guid.NewGuid();
            var eventId = Guid.NewGuid();
            values.Add($"('{order}', 'C{i}', 'SKU', 1, 'Pending', SYSUTCDATETIME(), SYSUTCDATETIME(), '{profile}', '{eventId}', '{order}', '{eventId}', '{order}')");
        }
        command.CommandText = $"INSERT INTO dbo.CustomerProfiles (Id, CustomerReference, Issuer, ObjectId, CreatedAt, UpdatedAt) VALUES ('{profile}', 'PROFILE', 'issuer', '{Guid.NewGuid()}', SYSUTCDATETIME(), SYSUTCDATETIME()); INSERT INTO dbo.Orders (Id, CustomerReference, ProductSku, Quantity, Status, CreatedAt, UpdatedAt, CustomerProfileId) VALUES {string.Join(',', values.Select(v => v[..v.IndexOf(", '", StringComparison.Ordinal)]))};";
        // Build inserts separately to keep each statement's columns explicit.
        command.CommandText = $"INSERT INTO dbo.CustomerProfiles (Id, CustomerReference, Issuer, ObjectId, CreatedAt, UpdatedAt) VALUES ('{profile}', 'PROFILE', 'issuer', '{Guid.NewGuid()}', SYSUTCDATETIME(), SYSUTCDATETIME());";
        foreach (var value in values)
        {
            var parts = value.Trim('(', ')').Split(", '", StringSplitOptions.None);
            var order = parts[0].Trim('\'');
            var customer = parts[1].Trim('\'');
            var eventId = parts[8].Trim('\'');
            command.CommandText += $" INSERT INTO dbo.Orders (Id, CustomerReference, ProductSku, Quantity, Status, CreatedAt, UpdatedAt, CustomerProfileId) VALUES ('{order}', '{customer}', 'SKU', 1, 'Pending', SYSUTCDATETIME(), SYSUTCDATETIME(), '{profile}'); INSERT INTO dbo.OutboxMessages (EventId, OrderId, AggregateId, MessageType, MessageVersion, Payload, OccurredAt, CreatedAt, AttemptCount) VALUES ('{eventId}', '{order}', '{order}', 'orders.order-created', 1, '{{\"eventId\":\"{eventId}\",\"orderId\":\"{order}\",\"messageType\":\"orders.order-created\",\"messageVersion\":1}}', SYSUTCDATETIME(), SYSUTCDATETIME(), 0);";
        }
        await command.ExecuteNonQueryAsync();
    }

    private sealed class TestContextFactory(DbContextOptions<CloudOrdersDbContext> options) : IDbContextFactory<CloudOrdersDbContext>
    {
        public CloudOrdersDbContext CreateDbContext() => new(options);
        public Task<CloudOrdersDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult<CloudOrdersDbContext>(new(options));
    }
}
