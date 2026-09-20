// Wms.Infrastructure/Data/Configurations/MovementConfiguration.cs

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;
using Wms.Domain.ValueObjects;

namespace Wms.Infrastructure.Data.Configurations;

public class MovementConfiguration : IEntityTypeConfiguration<Movement>
{
    public void Configure(EntityTypeBuilder<Movement> builder)
    {
        builder.ToTable("Movements");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Type)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(e => e.UserId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(e => e.SerialNumber)
            .HasMaxLength(100);

        builder.Property(e => e.ReferenceNumber)
            .HasMaxLength(100);

        builder.Property(e => e.Notes)
            .HasMaxLength(1000);

        builder.Property(e => e.Timestamp)
            .IsRequired()
            .HasColumnType("timestamp with time zone");

        builder.Property(e => e.CreatedAt)
            .IsRequired()
            .HasColumnType("timestamp with time zone");

        builder.Property(e => e.UpdatedAt)
            .HasColumnType("timestamp with time zone");

        // Value object configuration
        builder.Property(e => e.Quantity)
            .HasConversion(
                v => v.Value,
                v => new Quantity(v))
            .HasColumnType("decimal(28,12)")
            .IsRequired();

        builder.Property(e => e.EnteredQuantity)
            .HasColumnType("decimal(28,12)")
            .IsRequired();
        builder.Property(e => e.EnteredUnitOfMeasure)
            .IsRequired()
            .HasMaxLength(20);
        builder.Property(e => e.BaseUnitOfMeasure)
            .IsRequired()
            .HasMaxLength(20);
        builder.Property(e => e.ConversionFactorToBase)
            .HasColumnType("decimal(28,12)")
            .IsRequired();
        builder.Property(e => e.ConversionPrecision)
            .IsRequired();
        builder.Property(e => e.ConversionRoundingMode)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(e => e.ConversionRoundingDelta)
            .HasColumnType("decimal(28,12)")
            .IsRequired();
        builder.Property(e => e.ConversionPath)
            .IsRequired()
            .HasMaxLength(500);
        builder.Property(e => e.ConversionRuleIds)
            .IsRequired()
            .HasMaxLength(500);

        // Relationships
        builder.HasOne(e => e.Item)
            .WithMany()
            .HasForeignKey(e => e.ItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.FromLocation)
            .WithMany()
            .HasForeignKey(e => e.FromLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.ToLocation)
            .WithMany()
            .HasForeignKey(e => e.ToLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Lot)
            .WithMany()
            .HasForeignKey(e => e.LotId)
            .OnDelete(DeleteBehavior.Restrict);

        // Indexes
        builder.HasIndex(e => e.ItemId);
        builder.HasIndex(e => e.FromLocationId);
        builder.HasIndex(e => e.ToLocationId);
        builder.HasIndex(e => e.Type);
        builder.HasIndex(e => e.UserId);
        builder.HasIndex(e => e.Timestamp);
        builder.HasIndex(e => e.ReferenceNumber);
    }
}
