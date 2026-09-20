using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class InventoryStatusConfiguration : IEntityTypeConfiguration<InventoryStatus>
{
    public void Configure(EntityTypeBuilder<InventoryStatus> builder)
    {
        builder.ToTable("InventoryStatuses");
        builder.HasKey(status => status.Id);
        builder.Property(status => status.Code).HasMaxLength(50).IsRequired();
        builder.Property(status => status.Name).HasMaxLength(200).IsRequired();
        builder.Property(status => status.LocalizedName).HasMaxLength(200).IsRequired();
        builder.Property(status => status.DefaultLocationType).HasConversion<int>();
        builder.Property(status => status.IsAvailable).IsRequired();
        builder.Property(status => status.IsAllocatable).IsRequired();
        builder.Property(status => status.IsPickable).IsRequired();
        builder.Property(status => status.IsShippable).IsRequired();
        builder.Property(status => status.IsCountable).IsRequired();
        builder.Property(status => status.IsActive).IsRequired();
        builder.Property(status => status.IsSystem).IsRequired();
        builder.Property(status => status.ForceForLocationType).IsRequired();
        builder.Property(status => status.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(status => status.UpdatedAt)
            .HasColumnType("timestamp with time zone");

        builder.HasOne(status => status.Warehouse)
            .WithMany()
            .HasForeignKey(status => status.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(status => new { status.WarehouseId, status.Code })
            .IsUnique()
            .AreNullsDistinct(false);
        builder.HasIndex(status => status.WarehouseId);
        builder.HasIndex(status => status.IsActive);
        builder.HasIndex(status => status.DefaultLocationType);

        builder.HasData(InventoryStatusCatalog.SystemStatuses.Select(status => new
        {
            status.Id,
            status.Code,
            status.Name,
            status.LocalizedName,
            WarehouseId = (int?)null,
            status.IsAvailable,
            status.IsAllocatable,
            status.IsPickable,
            status.IsShippable,
            status.IsCountable,
            IsActive = true,
            IsSystem = true,
            status.DefaultLocationType,
            status.ForceForLocationType,
            CreatedAt = DateTime.UnixEpoch,
            UpdatedAt = (DateTime?)null
        }).ToArray());
    }
}
