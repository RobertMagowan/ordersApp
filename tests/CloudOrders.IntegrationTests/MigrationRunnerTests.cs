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
        Assert.DoesNotContain(database.ConnectionString, result.StandardError, StringComparison.Ordinal);
    }

    private static async Task<MigrationRunResult> RunRunnerAsync(string? connectionString, string? migration = null)
    {
        var runnerProject = Path.Combine(RepositoryRoot(), "src", "CloudOrders.Migrations", "CloudOrders.Migrations.csproj");
        var startInfo = new ProcessStartInfo("dotnet")
        {
            Arguments = $"run --project \"{runnerProject}\" --configuration Release --no-launch-profile" +
                (migration is null ? string.Empty : $" -- --migration {migration}"),
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
}
