using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class InventoryReplenishmentPolicyConfiguration
    : IEntityTypeConfiguration<InventoryReplenishmentPolicy>
{
    public void Configure(EntityTypeBuilder<InventoryReplenishmentPolicy> builder)
    {
        builder.ToTable("InventoryReplenishmentPolicies", table =>
            table.HasCheckConstraint(
                "CK_InventoryReplenishmentPolicies_QuantityOrder",
                "\"MinimumQuantity\" >= 0 AND \"SafetyStockQuantity\" >= 0 AND \"ReorderPointQuantity\" >= \"SafetyStockQuantity\" AND \"TargetQuantity\" >= \"ReorderPointQuantity\" AND \"MaximumQuantity\" >= \"TargetQuantity\""));

        builder.HasKey(policy => policy.Id);
        builder.Property(policy => policy.MinimumQuantity).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(policy => policy.MaximumQuantity).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(policy => policy.SafetyStockQuantity).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(policy => policy.ReorderPointQuantity).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(policy => policy.TargetQuantity).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(policy => policy.QuantityBasis).HasConversion<int>().IsRequired();
        builder.Property(policy => policy.PreferredSource).HasMaxLength(200);
        builder.Property(policy => policy.EffectiveFromUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(policy => policy.EffectiveToUtc)
            .HasColumnType("timestamp with time zone");
        builder.Property(policy => policy.Revision).IsRequired().IsConcurrencyToken();
        builder.Property(policy => policy.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(policy => policy.UpdatedAt)
            .HasColumnType("timestamp with time zone");

        builder.HasOne(policy => policy.Item)
            .WithMany()
            .HasForeignKey(policy => policy.ItemId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(policy => policy.Warehouse)
            .WithMany()
            .HasForeignKey(policy => policy.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(policy => policy.Location)
            .WithMany()
            .HasForeignKey(policy => new { policy.WarehouseId, policy.LocationId })
            .HasPrincipalKey(location => new { location.WarehouseId, location.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(policy => new
        {
            policy.WarehouseId,
            policy.ItemId,
            policy.LocationId,
            policy.EffectiveFromUtc
        });
        builder.HasIndex(policy => new
        {
            policy.WarehouseId,
            policy.ItemId,
            policy.IsActive,
            policy.EffectiveFromUtc,
            policy.EffectiveToUtc
        });
    }
}
