using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CloudOrders.Infrastructure.Persistence;

public sealed class CloudOrdersDesignTimeDbContextFactory : IDesignTimeDbContextFactory<CloudOrdersDbContext>
{
    public CloudOrdersDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__CloudOrders");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Set ConnectionStrings__CloudOrders before using EF Core design-time commands.");
        }

        var options = new DbContextOptionsBuilder<CloudOrdersDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new CloudOrdersDbContext(options);
    }
}
