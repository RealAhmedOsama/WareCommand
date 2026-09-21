using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class InventoryClassificationHistoryConfiguration
    : IEntityTypeConfiguration<InventoryClassificationHistory>
{
    public void Configure(EntityTypeBuilder<InventoryClassificationHistory> builder)
    {
        builder.ToTable("InventoryClassificationHistories");
        builder.HasKey(history => history.Id);

        builder.Property(history => history.PreviousClassification)
            .HasConversion<string>()
            .HasMaxLength(20);
        builder.Property(history => history.Classification)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(history => history.Source)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(history => history.MetricValue)
            .HasColumnType("decimal(28,12)")
            .IsRequired();
        builder.Property(history => history.CumulativePercent)
            .HasColumnType("decimal(9,4)")
            .IsRequired();
        builder.Property(history => history.ShippedQuantity)
            .HasColumnType("decimal(28,12)")
            .IsRequired();
        builder.Property(history => history.MovementQuantity)
            .HasColumnType("decimal(28,12)")
            .IsRequired();
        builder.Property(history => history.InventoryValue)
            .HasColumnType("decimal(28,12)")
            .IsRequired();
        builder.Property(history => history.CriticalityScore)
            .HasColumnType("decimal(28,12)")
            .IsRequired();
        builder.Property(history => history.LookbackFromUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(history => history.LookbackToUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(history => history.CalculationInputVersion)
            .HasMaxLength(50)
            .IsRequired();
        builder.Property(history => history.CalculationRunKey)
            .HasMaxLength(250)
            .IsRequired();
        builder.Property(history => history.Reason)
            .HasMaxLength(1_000)
            .IsRequired();
        builder.Property(history => history.ChangedAtUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(history => history.ManualOverrideExpiresAtUtc)
            .HasColumnType("timestamp with time zone");
        builder.Property(history => history.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne(history => history.Warehouse)
            .WithMany()
            .HasForeignKey(history => history.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(history => history.Item)
            .WithMany()
            .HasForeignKey(history => history.ItemId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(history => history.Policy)
            .WithMany()
            .HasForeignKey(history => history.PolicyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(history => new
        {
            history.WarehouseId,
            history.ItemId,
            history.ChangedAtUtc
        });
        builder.HasIndex(history => new
        {
            history.WarehouseId,
            history.ItemId,
            history.CalculationRunKey
        }).IsUnique();
    }
}
