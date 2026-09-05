using CloudOrders.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

string? targetMigration = null;
if (args.Length > 0)
{
    if (args.Length != 2 || !string.Equals(args[0], "--migration", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(args[1]))
    {
        Console.Error.WriteLine("SQL migration accepts only '--migration <migration-id>'.");
        return 1;
    }

    targetMigration = args[1];
}

var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__CloudOrders")
    ?? Environment.GetEnvironmentVariable("ConnectionStrings:CloudOrders");

if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("SQL migration requires configuration key ConnectionStrings:CloudOrders.");
    return 1;
}

try
{
    var options = new DbContextOptionsBuilder<CloudOrdersDbContext>()
        .UseSqlServer(connectionString)
        .Options;
    await using var context = new CloudOrdersDbContext(options);

    if (targetMigration is null)
    {
        await context.Database.MigrateAsync();
    }
    else
    {
        var resolvedTargetMigration = FindMigrationId(targetMigration);
        var appliedMigrationsBefore = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        var pendingMigrations = (await context.Database.GetPendingMigrationsAsync()).ToArray();
        var targetIsAlreadyApplied = appliedMigrationsBefore.Contains(resolvedTargetMigration, StringComparer.Ordinal);
        if (targetIsAlreadyApplied)
        {
            if (pendingMigrations.Length > 0)
            {
                throw new InvalidOperationException(
                    "Named SQL migration was already applied, but one or more later migrations are pending.");
            }
        }
        else
        {
            if (!pendingMigrations.SequenceEqual([resolvedTargetMigration], StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    "Named SQL migration must be the only pending migration before a migration-only release.");
            }

            await context.GetService<IMigrator>().MigrateAsync(resolvedTargetMigration);

            var appliedMigrationsAfter = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
            var appliedMigrationDelta = appliedMigrationsAfter
                .Except(appliedMigrationsBefore, StringComparer.Ordinal)
                .ToArray();
            if (!appliedMigrationDelta.SequenceEqual([resolvedTargetMigration], StringComparer.Ordinal))
            {
                throw new InvalidOperationException("Migration-only release applied an unexpected migration delta.");
            }
        }

        string FindMigrationId(string migrationSelector)
        {
            var matchingMigrationIds = context.Database.GetMigrations()
                .Where(migrationId =>
                    string.Equals(migrationId, migrationSelector, StringComparison.Ordinal) ||
                    migrationId.EndsWith($"_{migrationSelector}", StringComparison.Ordinal))
                .ToArray();

            return matchingMigrationIds.Length == 1
                ? matchingMigrationIds[0]
                : throw new InvalidOperationException("Migration selector must resolve to exactly one known migration.");
        }
    }

    Console.WriteLine("SQL migrations applied successfully.");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(
        $"SQL migration failed: category={GetFailureCategory(exception)}; exception={exception.GetType().Name}; detail={GetSafeFailureDetail(exception, connectionString)}.");
    return 1;
}

static string GetFailureCategory(Exception exception) => exception switch
{
    InvalidOperationException { Message: var message } when message.StartsWith("Migration selector", StringComparison.Ordinal) => "MIGRATION_SELECTOR_INVALID",
    InvalidOperationException { Message: var message } when message.StartsWith("Named SQL migration", StringComparison.Ordinal) => "MIGRATION_STATE_CONFLICT",
    InvalidOperationException { Message: var message } when message.StartsWith("Migration-only release", StringComparison.Ordinal) => "MIGRATION_STATE_CONFLICT",
    SqlException => "SQL_CONNECTION_OR_AUTHORIZATION",
    _ => "UNEXPECTED"
};

static string GetSafeFailureDetail(Exception exception, string connectionString) => exception switch
{
    InvalidOperationException => exception.Message.Replace(connectionString, "[REDACTED]", StringComparison.Ordinal),
    _ => "Unavailable"
};
