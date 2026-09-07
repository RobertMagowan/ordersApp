using System.Diagnostics;
using CloudOrders.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CloudOrders.IntegrationTests;

[Collection(SqlServerTestGroup.Name)]
public sealed class MigrationRunnerTests(SqlServerFixture sqlServerFixture)
{
    [Fact]
    public async Task MigrationRunnerAppliesCommittedMigrations()
    {
        await using var database = await sqlServerFixture.CreateEmptyDatabaseAsync();
        var result = await RunRunnerAsync(database.ConnectionString);

        var options = new DbContextOptionsBuilder<CloudOrdersDbContext>()
            .UseSqlServer(database.ConnectionString)
            .Options;
        await using var context = new CloudOrdersDbContext(options);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(
            "20260816221235_InitialSqlPersistence",
            await context.Database.GetAppliedMigrationsAsync(CancellationToken.None));
        Assert.Contains(
            "20260829075044_AddCustomerProfileOwnershipExpand",
            await context.Database.GetAppliedMigrationsAsync(CancellationToken.None));
        Assert.Contains(
            "EnforceCustomerProfileOwnership",
            await context.Database.GetAppliedMigrationsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task E2MakesOwnershipRequiredAndUsesActorKeyIdentityWhileRetainingSubjectId()
    {
        await using var database = await sqlServerFixture.CreateEmptyDatabaseAsync();
        var result = await RunRunnerAsync(database.ConnectionString);

        Assert.Equal(0, result.ExitCode);
        await using var context = new CloudOrdersDbContext(
            new DbContextOptionsBuilder<CloudOrdersDbContext>().UseSqlServer(database.ConnectionString).Options);

        var nullability = await context.Database.SqlQuery<ColumnShape>($"""
            SELECT TABLE_NAME AS TableName, COLUMN_NAME AS ColumnName, IS_NULLABLE AS IsNullable
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = 'dbo'
              AND ((TABLE_NAME = 'Orders' AND COLUMN_NAME = 'CustomerProfileId')
                OR (TABLE_NAME = 'IdempotencyRecords' AND COLUMN_NAME IN ('ActorCustomerProfileId', 'TargetCustomerProfileId', 'SubjectId')))
            """).ToListAsync();

        Assert.Equal("NO", nullability.Single(x => x.TableName == "Orders").IsNullable);
        Assert.Equal("NO", nullability.Single(x => x.ColumnName == "ActorCustomerProfileId").IsNullable);
        Assert.Equal("NO", nullability.Single(x => x.ColumnName == "TargetCustomerProfileId").IsNullable);
        Assert.Equal("YES", nullability.Single(x => x.ColumnName == "SubjectId").IsNullable);

        var keyColumns = await context.Database.SqlQuery<KeyColumn>($"""
            SELECT c.name AS ColumnName
            FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.object_id = OBJECT_ID(N'dbo.IdempotencyRecords') AND i.is_primary_key = 1
            ORDER BY ic.key_ordinal
            """).ToListAsync();

        Assert.Equal(["ActorCustomerProfileId", "IdempotencyKey"], keyColumns.Select(x => x.ColumnName));
    }

    [Fact]
    public async Task NamedMigrationRunnerSucceedsWhenTheNamedMigrationWasAlreadyAppliedAndNothingElseIsPending()
    {
        await using var database = await sqlServerFixture.CreateEmptyDatabaseAsync();
        var migration = "AddCustomerProfileOwnershipExpand";

        var initialResult = await RunRunnerAsync(database.ConnectionString);
        var rerunResult = await RunRunnerAsync(database.ConnectionString, migration);

        Assert.Equal(0, initialResult.ExitCode);
        Assert.Equal(0, rerunResult.ExitCode);
        Assert.Contains("SQL migrations applied successfully.", rerunResult.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MigrationRunnerFailsWhenConnectionStringIsMissing()
    {
        var result = await RunRunnerAsync(null);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("SQL migration requires configuration key ConnectionStrings:CloudOrders.", result.StandardError);
    }

    [Fact]
    public async Task MigrationRunnerFailsWhenMigrationCannotConnect()
    {
        var result = await RunRunnerAsync(
            "Server=localhost,1;Initial Catalog=CloudOrders;User ID=invalid;Password=invalid;Connect Timeout=1;Encrypt=False");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("SQL migration failed:", result.StandardError);
    }

    [Fact]
    public async Task NamedMigrationRunnerReportsASafeSelectorFailureCategory()
    {
        await using var database = await sqlServerFixture.CreateEmptyDatabaseAsync();

        var result = await RunRunnerAsync(database.ConnectionString, "MissingMigration");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("category=MIGRATION_SELECTOR_INVALID", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("exception=InvalidOperationException", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("detail=Migration selector must resolve to exactly one known migration.", result.StandardError, StringComparison.Ordinal);
        Assert.DoesNotContain(database.ConnectionString, result.StandardError, StringComparison.Ordinal);
    }

    private static async Task<MigrationRunResult> RunRunnerAsync(string? connectionString, string? migration = null)
    {
        var runnerAssembly = Path.Combine(
            RepositoryRoot(),
            "src",
            "CloudOrders.Migrations",
            "bin",
            "Release",
            "net10.0",
            "CloudOrders.Migrations.dll");
        Assert.True(File.Exists(runnerAssembly), $"Expected built migration runner at {runnerAssembly}.");
        var startInfo = new ProcessStartInfo("dotnet")
        {
            Arguments = $"\"{runnerAssembly}\"" +
                (migration is null ? string.Empty : $" --migration {migration}"),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.Environment.Remove("ConnectionStrings__CloudOrders");
        startInfo.Environment.Remove("ConnectionStrings:CloudOrders");

        if (connectionString is not null)
        {
            startInfo.Environment["ConnectionStrings__CloudOrders"] = connectionString;
        }

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(CancellationToken.None);

        return new MigrationRunResult(process.ExitCode, await standardOutput, await standardError);
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CloudOrders.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Unable to locate the repository root.");
    }

    private sealed record MigrationRunResult(int ExitCode, string StandardOutput, string StandardError);

    private sealed record ColumnShape(string TableName, string ColumnName, string IsNullable);

    private sealed record KeyColumn(string ColumnName);
}
