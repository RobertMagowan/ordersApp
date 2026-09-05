using Microsoft.EntityFrameworkCore;

namespace CloudOrders.Infrastructure.Persistence;

public sealed class CloudOrdersDbContext(DbContextOptions<CloudOrdersDbContext> options) : DbContext(options)
{
    internal DbSet<OrderEntity> Orders => Set<OrderEntity>();

    internal DbSet<OutboxMessageEntity> OutboxMessages => Set<OutboxMessageEntity>();

    internal DbSet<IdempotencyRecordEntity> IdempotencyRecords => Set<IdempotencyRecordEntity>();

    internal DbSet<CustomerProfileEntity> CustomerProfiles => Set<CustomerProfileEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CloudOrdersDbContext).Assembly);
}
