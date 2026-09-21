using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class InventoryClassificationPolicyConfiguration
    : IEntityTypeConfiguration<InventoryClassificationPolicy>
{
    public void Configure(EntityTypeBuilder<InventoryClassificationPolicy> builder)
    {
        builder.ToTable("InventoryClassificationPolicies");
        builder.HasKey(policy => policy.Id);

        builder.Property(policy => policy.PolicyKey)
            .HasMaxLength(80)
            .IsRequired();
        builder.Property(policy => policy.Method)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();
        builder.Property(policy => policy.LookbackDays).IsRequired();
        builder.Property(policy => policy.AThresholdPercent)
            .HasColumnType("decimal(9,4)")
            .IsRequired();
        builder.Property(policy => policy.BThresholdPercent)
            .HasColumnType("decimal(9,4)")
            .IsRequired();
        builder.Property(policy => policy.MinimumActivityValue)
            .HasColumnType("decimal(28,12)")
            .IsRequired();
        builder.Property(policy => policy.EffectiveFromUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(policy => policy.EffectiveToUtc)
            .HasColumnType("timestamp with time zone");
        builder.Property(policy => policy.IsActive).IsRequired();
        builder.Property(policy => policy.Revision)
            .IsRequired()
            .IsConcurrencyToken();
        builder.Property(policy => policy.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(policy => policy.UpdatedAt)
            .HasColumnType("timestamp with time zone");

        builder.HasOne(policy => policy.Warehouse)
            .WithMany()
            .HasForeignKey(policy => policy.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(policy => new { policy.WarehouseId, policy.PolicyKey })
            .IsUnique();
        builder.HasIndex(policy => new
        {
            policy.WarehouseId,
            policy.IsActive,
            policy.EffectiveFromUtc,
            policy.EffectiveToUtc
        });
    }
}
