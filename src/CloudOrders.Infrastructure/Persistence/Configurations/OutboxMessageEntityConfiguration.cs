using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CloudOrders.Infrastructure.Persistence.Configurations;

internal sealed class OutboxMessageEntityConfiguration : IEntityTypeConfiguration<OutboxMessageEntity>
{
    public void Configure(EntityTypeBuilder<OutboxMessageEntity> builder)
    {
        builder.ToTable("OutboxMessages", "dbo");
        builder.HasKey(message => message.EventId);
        builder.Property(message => message.EventId).ValueGeneratedNever();
        builder.Property(message => message.OrderId).IsRequired();
        builder.Property(message => message.AggregateId).IsRequired();
        builder.Property(message => message.MessageType).HasMaxLength(256).IsRequired();
        builder.Property(message => message.MessageVersion).IsRequired();
        builder.Property(message => message.Payload).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(message => message.OccurredAt).HasPrecision(7);
        builder.Property(message => message.CreatedAt).HasPrecision(7);
        builder.Property(message => message.ProcessedAt).HasPrecision(7);
        builder.Property(message => message.LastAttemptAt).HasPrecision(7);
        builder.Property(message => message.LastErrorCode).HasMaxLength(128);
        builder.Property(message => message.TraceParent).HasMaxLength(512);
        builder.HasOne(message => message.Order)
            .WithMany()
            .HasForeignKey(message => message.OrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(message => new { message.CreatedAt, message.EventId })
            .HasFilter("[ProcessedAt] IS NULL")
            .IncludeProperties(message => new
            {
                message.MessageType,
                message.MessageVersion,
                message.OccurredAt,
                message.AttemptCount
            })
            .HasDatabaseName("IX_OutboxMessages_Pending");
    }
}
