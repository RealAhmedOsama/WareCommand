using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class ShipmentPackageLinkConfiguration : IEntityTypeConfiguration<ShipmentPackageLink>
{
    public void Configure(EntityTypeBuilder<ShipmentPackageLink> builder)
    {
        builder.ToTable("ShipmentPackageLinks");
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(value => value.LoadedByUserId).HasMaxLength(450);
        builder.Property(value => value.LoadedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(value => value.ShippedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(value => value.Revision).IsConcurrencyToken();
        builder.HasOne(value => value.Shipment).WithMany(value => value.Packages).HasForeignKey(value => value.ShipmentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(value => value.ShipmentPackage).WithMany().HasForeignKey(value => value.ShipmentPackageId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.ShipmentLoad).WithMany().HasForeignKey(value => value.ShipmentLoadId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new { value.ShipmentId, value.ShipmentPackageId }).IsUnique();
        builder.HasIndex(value => value.ShipmentPackageId).IsUnique();
    }
}
