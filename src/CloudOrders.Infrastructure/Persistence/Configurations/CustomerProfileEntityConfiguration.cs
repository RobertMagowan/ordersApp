using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CloudOrders.Infrastructure.Persistence.Configurations;

internal sealed class CustomerProfileEntityConfiguration : IEntityTypeConfiguration<CustomerProfileEntity>
{
    public void Configure(EntityTypeBuilder<CustomerProfileEntity> builder)
    {
        builder.ToTable("CustomerProfiles", "dbo");
        builder.HasKey(profile => profile.Id).HasName("PK_CustomerProfiles");
        builder.Property(profile => profile.Id).ValueGeneratedNever();
        builder.Property(profile => profile.Issuer)
            .HasMaxLength(256)
            .UseCollation("Latin1_General_100_BIN2")
            .IsRequired();
        builder.Property(profile => profile.ObjectId).IsRequired();
        builder.Property(profile => profile.CustomerReference)
            .HasColumnType("varchar(64)")
            .UseCollation("Latin1_General_100_BIN2")
            .IsRequired();
        builder.Property(profile => profile.ContactEmail)
            .HasMaxLength(320)
            .UseCollation("Latin1_General_100_CI_AS_SC");
        builder.Property(profile => profile.CreatedAt).HasPrecision(7);
        builder.Property(profile => profile.UpdatedAt).HasPrecision(7);
        builder.Property(profile => profile.RowVersion).IsRowVersion();
        builder.HasAlternateKey(profile => new { profile.Issuer, profile.ObjectId })
            .HasName("AK_CustomerProfiles_Issuer_ObjectId");
        builder.HasAlternateKey(profile => profile.CustomerReference)
            .HasName("AK_CustomerProfiles_CustomerReference");
    }
}
