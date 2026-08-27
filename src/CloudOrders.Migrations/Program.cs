using CloudOrders.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

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

    await context.Database.MigrateAsync();
    Console.WriteLine("SQL migrations applied successfully.");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"SQL migration failed: {exception.GetType().Name}.");
    return 1;
}
