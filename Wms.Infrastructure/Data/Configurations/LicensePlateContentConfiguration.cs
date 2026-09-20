using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;
using Wms.Domain.ValueObjects;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class LicensePlateContentConfiguration : IEntityTypeConfiguration<LicensePlateContent>
{
    public void Configure(EntityTypeBuilder<LicensePlateContent> builder)
    {
        builder.ToTable("LicensePlateContents", table => table.HasCheckConstraint(
            "CK_LicensePlateContents_QuantityPositive",
            "\"Quantity\" > 0"));

        builder.HasKey(content => content.Id);
        builder.Property(content => content.Quantity)
            .HasConversion(value => value.Value, value => new Quantity(value))
            .HasColumnType("decimal(28,12)")
            .IsRequired();
        builder.Property(content => content.CreatedAt)
            .IsRequired()
            .HasColumnType("timestamp with time zone");
        builder.Property(content => content.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .IsConcurrencyToken();
        builder.Property(content => content.Revision)
            .IsRequired()
            .IsConcurrencyToken();

        builder.HasOne(content => content.LicensePlate)
            .WithMany(plate => plate.Contents)
            .HasForeignKey(content => content.LicensePlateId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(content => content.Item)
            .WithMany()
            .HasForeignKey(content => content.ItemId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(content => content.Lot)
            .WithMany()
            .HasForeignKey(content => content.LotId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(content => content.SerialNumber)
            .WithMany()
            .HasForeignKey(content => content.SerialNumberId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(content => content.InventoryStatus)
            .WithMany()
            .HasForeignKey(content => content.InventoryStatusId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(content => content.ItemPackaging)
            .WithMany()
            .HasForeignKey(content => content.ItemPackagingId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(content => new
        {
            content.LicensePlateId,
            content.ItemId,
            content.LotId,
            content.SerialNumberId,
            content.InventoryStatusId,
            content.ItemPackagingId
        })
            .IsUnique()
            .AreNullsDistinct(false);
        builder.HasIndex(content => content.ItemId);
        builder.HasIndex(content => content.LotId);
        builder.HasIndex(content => content.SerialNumberId);
        builder.HasIndex(content => content.InventoryStatusId);
    }
}
