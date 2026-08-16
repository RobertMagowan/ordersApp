using CloudOrders.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CloudOrders.Infrastructure.Persistence.Configurations;

internal sealed class OrderEntityConfiguration : IEntityTypeConfiguration<OrderEntity>
{
    public void Configure(EntityTypeBuilder<OrderEntity> builder)
    {
        builder.ToTable("Orders", "dbo", table =>
        {
            table.HasCheckConstraint("CK_Orders_Quantity", "[Quantity] >= 1 AND [Quantity] <= 100");
            table.HasCheckConstraint("CK_Orders_Status", "[Status] IN (N'Pending', N'Processing')");
        });
        builder.HasKey(order => order.Id);
        builder.Property(order => order.Id).ValueGeneratedNever();
        builder.Property(order => order.CustomerReference).HasMaxLength(64).IsRequired();
        builder.Property(order => order.ProductSku).HasMaxLength(64).IsRequired();
        builder.Property(order => order.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(order => order.CreatedAt).HasPrecision(7);
        builder.Property(order => order.UpdatedAt).HasPrecision(7);
        builder.Property(order => order.RowVersion).IsRowVersion();
        builder.HasIndex(order => new { order.CustomerReference, order.CreatedAt, order.Id })
            .IsDescending(false, true, true)
            .HasDatabaseName("IX_Orders_CustomerReference_CreatedAt_Id");
    }
}
