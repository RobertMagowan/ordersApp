using CloudOrders.Infrastructure.Persistence;
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
        await context.GetService<IMigrator>().MigrateAsync(targetMigration);
        var appliedMigrations = await context.Database.GetAppliedMigrationsAsync();
        if (!appliedMigrations.Contains(targetMigration, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"Named SQL migration was not applied: {targetMigration}.");
        }
    }

    Console.WriteLine("SQL migrations applied successfully.");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"SQL migration failed: {exception.GetType().Name}.");
    return 1;
}
