using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class PickingStrategyPolicyConfiguration : IEntityTypeConfiguration<PickingStrategyPolicy>
{
    public void Configure(EntityTypeBuilder<PickingStrategyPolicy> builder)
    {
        builder.ToTable("PickingStrategyPolicies");
        builder.HasKey(policy => policy.Id);
        builder.Property(policy => policy.PolicyKey).HasMaxLength(80).IsRequired();
        builder.Property(policy => policy.Name).HasMaxLength(200).IsRequired();
        builder.Property(policy => policy.Strategy).HasConversion<int>().IsRequired();
        builder.Property(policy => policy.OrderProfileCode).HasMaxLength(80);
        builder.Property(policy => policy.PackageProfileCode).HasMaxLength(80);
        builder.Property(policy => policy.SequenceMode).HasMaxLength(50).IsRequired();
        builder.Property(policy => policy.MaxWeightKg).HasColumnType("decimal(28,12)");
        builder.Property(policy => policy.MaxVolumeCubicMeters).HasColumnType("decimal(28,12)");
        builder.Property(policy => policy.EffectiveFromUtc).HasColumnType("timestamp with time zone");
        builder.Property(policy => policy.EffectiveToUtc).HasColumnType("timestamp with time zone");
        builder.Property(policy => policy.Revision).IsRequired().IsConcurrencyToken();
        builder.Property(policy => policy.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(policy => policy.UpdatedAt).HasColumnType("timestamp with time zone");

        builder.HasOne(policy => policy.Warehouse)
            .WithMany()
            .HasForeignKey(policy => policy.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(policy => policy.WaveTemplate)
            .WithMany()
            .HasForeignKey(policy => policy.WaveTemplateId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(policy => policy.Item)
            .WithMany()
            .HasForeignKey(policy => policy.ItemId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(policy => policy.LocationZone)
            .WithMany()
            .HasForeignKey(policy => policy.LocationZoneId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(policy => new { policy.WarehouseId, policy.PolicyKey }).IsUnique();
        builder.HasIndex(policy => new { policy.WarehouseId, policy.IsActive, policy.Priority });
    }
}
