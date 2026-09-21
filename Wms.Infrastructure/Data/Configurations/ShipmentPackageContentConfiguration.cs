using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class ShipmentPackageContentConfiguration : IEntityTypeConfiguration<ShipmentPackageContent>
{
    public void Configure(EntityTypeBuilder<ShipmentPackageContent> builder)
    {
        builder.ToTable("ShipmentPackageContents");
        builder.HasKey(content => content.Id);
        builder.Property(content => content.Quantity).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(content => content.BaseUnitOfMeasure).IsRequired().HasMaxLength(20);
        builder.Property(content => content.SerialNumber).HasMaxLength(100);
        builder.Property(content => content.ExpectedWeightKg).HasColumnType("decimal(28,12)");
        builder.Property(content => content.OwnerKind).HasConversion<int>().IsRequired();
        builder.Property(content => content.OwnerCodeSnapshot).HasMaxLength(80).IsRequired();
        builder.Property(content => content.Revision).IsRequired().IsConcurrencyToken();
        builder.Property(content => content.CreatedAt).IsRequired().HasColumnType("timestamp with time zone");
        builder.Property(content => content.UpdatedAt).HasColumnType("timestamp with time zone");

        builder.HasOne(content => content.ShipmentPackage)
            .WithMany(package => package.Contents)
            .HasForeignKey(content => content.ShipmentPackageId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(content => content.SalesOrderLine)
            .WithMany()
            .HasForeignKey(content => content.SalesOrderLineId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(content => content.Item)
            .WithMany()
            .HasForeignKey(content => content.ItemId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(content => content.SourceLocation)
            .WithMany()
            .HasForeignKey(content => content.SourceLocationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(content => content.SourceLicensePlate)
            .WithMany()
            .HasForeignKey(content => content.SourceLicensePlateId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(content => content.Lot)
            .WithMany()
            .HasForeignKey(content => content.LotId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(content => content.SerialNumberEntity)
            .WithMany()
            .HasForeignKey(content => content.SerialNumberId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(content => content.InventoryStatus)
            .WithMany()
            .HasForeignKey(content => content.InventoryStatusId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(content => content.InventoryOwner)
            .WithMany()
            .HasForeignKey(content => content.InventoryOwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(content => new
        {
            content.ShipmentPackageId,
            content.SalesOrderLineId,
            content.ItemId,
            content.LotId,
            content.SerialNumberId,
            content.SourceLicensePlateId,
            content.OwnerKind,
            content.InventoryOwnerId
        });
        builder.HasIndex(content => new { content.SalesOrderLineId, content.ItemId });
    }
}
