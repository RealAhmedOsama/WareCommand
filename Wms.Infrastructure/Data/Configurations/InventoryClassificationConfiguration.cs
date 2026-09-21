using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class InventoryClassificationConfiguration
    : IEntityTypeConfiguration<InventoryClassification>
{
    public void Configure(EntityTypeBuilder<InventoryClassification> builder)
    {
        builder.ToTable("InventoryClassifications");
        builder.HasKey(classification => classification.Id);

        builder.Property(classification => classification.Classification)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(classification => classification.Source)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(classification => classification.MetricValue)
            .HasColumnType("decimal(28,12)")
            .IsRequired();
        builder.Property(classification => classification.CumulativePercent)
            .HasColumnType("decimal(9,4)")
            .IsRequired();
        builder.Property(classification => classification.ShippedQuantity)
            .HasColumnType("decimal(28,12)")
            .IsRequired();
        builder.Property(classification => classification.MovementQuantity)
            .HasColumnType("decimal(28,12)")
            .IsRequired();
        builder.Property(classification => classification.InventoryValue)
            .HasColumnType("decimal(28,12)")
            .IsRequired();
        builder.Property(classification => classification.CriticalityScore)
            .HasColumnType("decimal(28,12)")
            .IsRequired();
        builder.Property(classification => classification.LookbackFromUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(classification => classification.LookbackToUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(classification => classification.CalculationInputVersion)
            .HasMaxLength(50)
            .IsRequired();
        builder.Property(classification => classification.CalculationRunKey)
            .HasMaxLength(250)
            .IsRequired();
        builder.Property(classification => classification.CalculatedAtUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(classification => classification.ManualOverrideReason)
            .HasMaxLength(1_000);
        builder.Property(classification => classification.ManualOverrideExpiresAtUtc)
            .HasColumnType("timestamp with time zone");
        builder.Property(classification => classification.Revision)
            .IsRequired()
            .IsConcurrencyToken();
        builder.Property(classification => classification.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(classification => classification.UpdatedAt)
            .HasColumnType("timestamp with time zone");

        builder.HasOne(classification => classification.Warehouse)
            .WithMany()
            .HasForeignKey(classification => classification.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(classification => classification.Item)
            .WithMany()
            .HasForeignKey(classification => classification.ItemId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(classification => classification.Policy)
            .WithMany()
            .HasForeignKey(classification => classification.PolicyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(classification => new
        {
            classification.WarehouseId,
            classification.ItemId
        }).IsUnique();
        builder.HasIndex(classification => new
        {
            classification.WarehouseId,
            classification.Classification
        });
        builder.HasIndex(classification => classification.ManualOverrideExpiresAtUtc);
    }
}
