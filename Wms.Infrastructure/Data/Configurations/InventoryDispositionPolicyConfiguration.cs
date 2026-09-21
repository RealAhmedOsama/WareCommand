using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class InventoryDispositionPolicyConfiguration
    : IEntityTypeConfiguration<InventoryDispositionPolicy>
{
    public void Configure(EntityTypeBuilder<InventoryDispositionPolicy> builder)
    {
        builder.ToTable("InventoryDispositionPolicies");
        builder.HasKey(policy => policy.Id);
        builder.Property(policy => policy.PolicyKey).HasMaxLength(80).IsRequired();
        builder.Property(policy => policy.Name).HasMaxLength(200).IsRequired();
        builder.Property(policy => policy.ItemCategory).HasMaxLength(100);
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

        builder.HasOne(policy => policy.Warehouse)
            .WithMany()
            .HasForeignKey(policy => policy.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(policy => policy.Item)
            .WithMany()
            .HasForeignKey(policy => policy.ItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(policy => new { policy.WarehouseId, policy.PolicyKey }).IsUnique();
        builder.HasIndex(policy => new
        {
            policy.WarehouseId,
            policy.IsActive,
            policy.EffectiveFromUtc,
            policy.EffectiveToUtc,
            policy.Priority,
            policy.ItemId,
            policy.ItemCategory
        });
    }
}
