using Microsoft.EntityFrameworkCore;

namespace CloudOrders.Infrastructure.Persistence;

public sealed class CloudOrdersDbContext(DbContextOptions<CloudOrdersDbContext> options) : DbContext(options);
