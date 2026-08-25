using CloudOrders.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

namespace CloudOrders.IntegrationTests;

public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer container =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04").Build();

    public Task InitializeAsync() => container.StartAsync();

    public Task DisposeAsync() => container.DisposeAsync().AsTask();

    public async Task<TestDatabase> CreateDatabaseAsync()
    {
        var database = await CreateEmptyDatabaseAsync();
        var options = new DbContextOptionsBuilder<CloudOrdersDbContext>()
            .UseSqlServer(database.ConnectionString)
            .Options;

        await using var context = new CloudOrdersDbContext(options);
        await context.Database.MigrateAsync();

        return database;
    }

    public async Task<TestDatabase> CreateEmptyDatabaseAsync()
    {
        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(container.GetConnectionString())
        {
            InitialCatalog = $"CloudOrders_{Guid.NewGuid():N}"
        };

        var databaseName = builder.InitialCatalog;
        var masterConnectionString = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(container.GetConnectionString())
        {
            InitialCatalog = "master"
        }.ConnectionString;
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(masterConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{databaseName}]";
        await command.ExecuteNonQueryAsync();

        return new TestDatabase(builder.ConnectionString);
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
