using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class CarrierServiceConfiguration : IEntityTypeConfiguration<CarrierService>
{
    public void Configure(EntityTypeBuilder<CarrierService> builder)
    {
        builder.ToTable("CarrierServices");
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Code).HasMaxLength(80).IsRequired();
        builder.Property(value => value.Name).HasMaxLength(200).IsRequired();
        builder.Property(value => value.Revision).IsConcurrencyToken();
        builder.HasOne(value => value.Carrier)
            .WithMany(value => value.Services)
            .HasForeignKey(value => value.CarrierId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new { value.CarrierId, value.Code }).IsUnique();
    }
}
