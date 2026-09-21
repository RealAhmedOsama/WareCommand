using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class CarrierConfiguration : IEntityTypeConfiguration<Carrier>
{
    public void Configure(EntityTypeBuilder<Carrier> builder)
    {
        builder.ToTable("Carriers");
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Code).HasMaxLength(50).IsRequired();
        builder.Property(value => value.Name).HasMaxLength(200).IsRequired();
        builder.Property(value => value.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(value => value.TrackingUrlTemplate).HasMaxLength(500);
        builder.Property(value => value.Revision).IsConcurrencyToken();
        builder.HasIndex(value => value.Code).IsUnique();
    }
}
