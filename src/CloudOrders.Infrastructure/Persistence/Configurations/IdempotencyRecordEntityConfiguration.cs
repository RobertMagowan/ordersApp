using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CloudOrders.Infrastructure.Persistence.Configurations;

internal sealed class IdempotencyRecordEntityConfiguration : IEntityTypeConfiguration<IdempotencyRecordEntity>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecordEntity> builder)
    {
        builder.ToTable("IdempotencyRecords", "dbo");
        builder.HasKey(record => new { record.SubjectId, record.IdempotencyKey });
        builder.Property(record => record.SubjectId).HasMaxLength(200).IsRequired();
        builder.Property(record => record.IdempotencyKey).ValueGeneratedNever();
        builder.Property(record => record.RequestHash).HasColumnType("binary(32)").IsRequired();
        builder.Property(record => record.ResponseJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(record => record.CreatedAt).HasPrecision(7);
        builder.Property(record => record.ExpiresAt).HasPrecision(7);
        builder.HasOne(record => record.Order)
            .WithMany()
            .HasForeignKey(record => record.OrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(record => record.OrderId).HasDatabaseName("IX_IdempotencyRecords_OrderId");
        builder.HasIndex(record => record.ExpiresAt).HasDatabaseName("IX_IdempotencyRecords_ExpiresAt");
        builder.HasOne<CustomerProfileEntity>()
            .WithMany()
            .HasForeignKey(record => record.ActorCustomerProfileId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_IdempotencyRecords_CustomerProfiles_ActorCustomerProfileId");
        builder.HasOne<CustomerProfileEntity>()
            .WithMany()
            .HasForeignKey(record => record.TargetCustomerProfileId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_IdempotencyRecords_CustomerProfiles_TargetCustomerProfileId");
        builder.HasIndex(record => record.ActorCustomerProfileId)
            .HasDatabaseName("IX_IdempotencyRecords_ActorCustomerProfileId");
        builder.HasIndex(record => record.TargetCustomerProfileId)
            .HasDatabaseName("IX_IdempotencyRecords_TargetCustomerProfileId");
    }
}
