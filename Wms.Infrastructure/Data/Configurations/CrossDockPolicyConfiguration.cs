using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class CrossDockPolicyConfiguration : IEntityTypeConfiguration<CrossDockPolicy>
{
    public void Configure(EntityTypeBuilder<CrossDockPolicy> builder)
    {
        builder.ToTable("CrossDockPolicies");
        builder.HasKey(policy => policy.Id);
        builder.Property(policy => policy.PolicyKey).HasMaxLength(80).IsRequired();
        builder.Property(policy => policy.Name).HasMaxLength(200).IsRequired();
        builder.Property(policy => policy.ItemCategory).HasMaxLength(100);
        builder.Property(policy => policy.InboundSourceType).HasMaxLength(30);
        builder.Property(policy => policy.QuantityTolerancePercent).HasColumnType("decimal(9,4)").IsRequired();
        builder.Property(policy => policy.EffectiveFromUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(policy => policy.EffectiveToUtc).HasColumnType("timestamp with time zone");
        builder.Property(policy => policy.IsActive).IsRequired();
        builder.Property(policy => policy.Revision).IsRequired().IsConcurrencyToken();
        builder.Property(policy => policy.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(policy => policy.UpdatedAt).HasColumnType("timestamp with time zone");

        builder.HasOne(policy => policy.Warehouse)
            .WithMany()
            .HasForeignKey(policy => policy.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(policy => policy.Item)
            .WithMany()
            .HasForeignKey(policy => policy.ItemId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(policy => policy.Supplier)
            .WithMany()
            .HasForeignKey(policy => policy.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(policy => policy.Customer)
            .WithMany()
            .HasForeignKey(policy => policy.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(policy => policy.SalesOrder)
            .WithMany()
            .HasForeignKey(policy => policy.SalesOrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(policy => policy.DestinationLocation)
            .WithMany()
            .HasForeignKey(policy => new { policy.WarehouseId, policy.DestinationLocationId })
            .HasPrincipalKey(location => new { location.WarehouseId, location.Id })
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
            policy.ItemCategory,
            policy.SupplierId,
            policy.CustomerId,
            policy.SalesOrderId
        });
    }
}
