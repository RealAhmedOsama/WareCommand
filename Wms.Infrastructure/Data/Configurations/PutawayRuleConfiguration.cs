using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class PutawayRuleConfiguration : IEntityTypeConfiguration<PutawayRule>
{
    public void Configure(EntityTypeBuilder<PutawayRule> builder)
    {
        builder.ToTable("PutawayRules");

        builder.HasKey(rule => rule.Id);
        builder.Property(rule => rule.Code).HasMaxLength(80).IsRequired();
        builder.Property(rule => rule.Name).HasMaxLength(200).IsRequired();
        builder.Property(rule => rule.Strategy)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();
        builder.Property(rule => rule.EffectiveFromUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(rule => rule.EffectiveToUtc)
            .HasColumnType("timestamp with time zone");
        builder.Property(rule => rule.LicensePlateType)
            .HasConversion<string>()
            .HasMaxLength(30);
        builder.Property(rule => rule.LotStatus)
            .HasConversion<string>()
            .HasMaxLength(30);
        builder.Property(rule => rule.ItemCategory).HasMaxLength(100);
        builder.Property(rule => rule.PackageType).HasMaxLength(50);
        builder.Property(rule => rule.HazardClass).HasMaxLength(100);
        builder.Property(rule => rule.StorageProfile).HasMaxLength(100);
        builder.Property(rule => rule.SourceProcess).HasMaxLength(50);
        builder.Property(rule => rule.TargetStorageProfile).HasMaxLength(100);
        builder.Property(rule => rule.Notes).HasMaxLength(2_000);
        builder.Property(rule => rule.MinimumTemperatureCelsius).HasColumnType("decimal(18,4)");
        builder.Property(rule => rule.MaximumTemperatureCelsius).HasColumnType("decimal(18,4)");
        builder.Property(rule => rule.Revision).IsConcurrencyToken();

        builder.HasOne(rule => rule.Warehouse)
            .WithMany()
            .HasForeignKey(rule => rule.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(rule => rule.Item)
            .WithMany()
            .HasForeignKey(rule => rule.ItemId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(rule => rule.Supplier)
            .WithMany()
            .HasForeignKey(rule => rule.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(rule => rule.FixedLocation)
            .WithMany()
            .HasForeignKey(rule => rule.FixedLocationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(rule => rule.FallbackLocation)
            .WithMany()
            .HasForeignKey(rule => rule.FallbackLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(rule => new { rule.WarehouseId, rule.Code }).IsUnique();
        builder.HasIndex(rule => new
        {
            rule.WarehouseId,
            rule.IsActive,
            rule.IsSimulation,
            rule.EffectiveFromUtc,
            rule.EffectiveToUtc,
            rule.Priority
        });
        builder.HasIndex(rule => new { rule.WarehouseId, rule.ItemId, rule.ItemCategory, rule.SourceProcess });
    }
}
