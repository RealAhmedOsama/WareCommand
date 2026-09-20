using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class InventoryBalanceConfiguration : IEntityTypeConfiguration<InventoryBalance>
{
    private static readonly string[] IdentityColumns =
    [
        "WarehouseId",
        "LocationId",
        "ItemId",
        "LotId",
        "SerialNumberId",
        "SerialNumber",
        "LicensePlateId",
        "InventoryStatusId",
        "BaseUnitOfMeasure"
    ];

    public void Configure(EntityTypeBuilder<InventoryBalance> builder)
    {
        builder.ToTable("InventoryBalances", table =>
            table.HasCheckConstraint(
                "CK_InventoryBalances_ReservedNonNegative",
                "\"ReservedQuantity\" >= 0"));

        builder.HasKey(balance => balance.Id);
        builder.Property(balance => balance.BaseUnitOfMeasure)
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(balance => balance.SerialNumber)
            .HasMaxLength(100);
        builder.Property(balance => balance.OnHandQuantity)
            .HasColumnType("decimal(28,12)")
            .IsRequired();
        builder.Property(balance => balance.ReservedQuantity)
            .HasColumnType("decimal(28,12)")
            .IsRequired();
        builder.Property(balance => balance.Revision)
            .IsRequired()
            .IsConcurrencyToken();
        builder.Property(balance => balance.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(balance => balance.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .IsConcurrencyToken();
        builder.Ignore(balance => balance.AvailableQuantity);

        builder.HasOne(balance => balance.Warehouse)
            .WithMany()
            .HasForeignKey(balance => balance.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(balance => balance.Location)
            .WithMany()
            .HasForeignKey(balance => new { balance.WarehouseId, balance.LocationId })
            .HasPrincipalKey(location => new { location.WarehouseId, location.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(balance => balance.Item)
            .WithMany()
            .HasForeignKey(balance => balance.ItemId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(balance => balance.Lot)
            .WithMany()
            .HasForeignKey(balance => balance.LotId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(balance => balance.Serial)
            .WithMany()
            .HasForeignKey(balance => balance.SerialNumberId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(balance => balance.LicensePlate)
            .WithMany()
            .HasForeignKey(balance => balance.LicensePlateId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(balance => balance.InventoryStatus)
            .WithMany()
            .HasForeignKey(balance => balance.InventoryStatusId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(IdentityColumns)
            .IsUnique()
            .AreNullsDistinct(false);
        builder.HasIndex(balance => new { balance.WarehouseId, balance.ItemId });
        builder.HasIndex(balance => new { balance.WarehouseId, balance.LocationId });
        builder.HasIndex(balance => balance.LotId);
        builder.HasIndex(balance => balance.SerialNumberId);
        builder.HasIndex(balance => balance.LicensePlateId);
        builder.HasIndex(balance => balance.InventoryStatusId);
    }
}
