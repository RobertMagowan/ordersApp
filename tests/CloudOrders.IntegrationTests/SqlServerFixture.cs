using CloudOrders.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

namespace CloudOrders.IntegrationTests;

public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public Task InitializeAsync() => container.StartAsync();

    public Task DisposeAsync() => container.DisposeAsync().AsTask();

    public async Task<TestDatabase> CreateDatabaseAsync()
    {
        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(container.GetConnectionString())
        {
            InitialCatalog = $"CloudOrders_{Guid.NewGuid():N}"
        };

        var connectionString = builder.ConnectionString;
        var options = new DbContextOptionsBuilder<CloudOrdersDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        await using var context = new CloudOrdersDbContext(options);
        await context.Database.MigrateAsync();

        return new TestDatabase(connectionString);
    }
}

[CollectionDefinition(Name)]
public sealed class SqlServerTestGroup : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "SQL Server";
}

public sealed class TestDatabase(string connectionString) : IAsyncDisposable
{
    public string ConnectionString { get; } = connectionString;

    public async ValueTask DisposeAsync()
    {
        var options = new DbContextOptionsBuilder<CloudOrdersDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        await using var context = new CloudOrdersDbContext(options);
        await context.Database.EnsureDeletedAsync();
    }
}
