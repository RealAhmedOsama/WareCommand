using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class WaveConfiguration : IEntityTypeConfiguration<Wave>
{
    public void Configure(EntityTypeBuilder<Wave> builder)
    {
        builder.ToTable("Waves");
        builder.HasKey(wave => wave.Id);
        builder.Property(wave => wave.WaveNumber).HasMaxLength(80).IsRequired();
        builder.Property(wave => wave.CreationKey).HasMaxLength(250).IsRequired();
        builder.Property(wave => wave.TemplateKey).HasMaxLength(80);
        builder.Property(wave => wave.TriggerType).HasConversion<int>().IsRequired();
        builder.Property(wave => wave.Priority).IsRequired();
        builder.Property(wave => wave.PlannedStartAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(wave => wave.PlannedReleaseAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(wave => wave.CriteriaJson).HasMaxLength(8_000).IsRequired();
        builder.Property(wave => wave.Status).HasConversion<int>().IsRequired();
        builder.Property(wave => wave.CreatedByUserId).HasMaxLength(450).IsRequired();
        builder.Property(wave => wave.LastProcessIdempotencyKey).HasMaxLength(250);
        builder.Property(wave => wave.LastProcessedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(wave => wave.LastError).HasMaxLength(2_000);
        builder.Property(wave => wave.Revision).IsRequired().IsConcurrencyToken();
        builder.Property(wave => wave.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(wave => wave.UpdatedAt).HasColumnType("timestamp with time zone");

        builder.HasOne(wave => wave.Warehouse)
            .WithMany()
            .HasForeignKey(wave => wave.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(wave => wave.Template)
            .WithMany()
            .HasForeignKey(wave => wave.TemplateId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(wave => wave.Lines)
            .WithOne(line => line.Wave)
            .HasForeignKey(line => line.WaveId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(wave => wave.History)
            .WithOne(history => history.Wave)
            .HasForeignKey(history => history.WaveId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(wave => wave.WaveNumber).IsUnique();
        builder.HasIndex(wave => new { wave.WarehouseId, wave.CreationKey }).IsUnique();
        builder.HasIndex(wave => new { wave.WarehouseId, wave.Status, wave.PlannedReleaseAtUtc });
    }
}
