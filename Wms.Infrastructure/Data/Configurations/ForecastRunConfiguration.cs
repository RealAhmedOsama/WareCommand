using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Infrastructure.Forecasting;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class ForecastRunConfiguration : IEntityTypeConfiguration<ForecastRunEntity>
{
    public void Configure(EntityTypeBuilder<ForecastRunEntity> builder)
    {
        builder.ToTable("ForecastRuns");
        builder.HasKey(run => run.Id);
        builder.Property(run => run.Granularity).HasMaxLength(20).IsRequired();
        builder.Property(run => run.DataStatus).HasMaxLength(30).IsRequired();
        builder.Property(run => run.SelectedModel).HasMaxLength(50);
        builder.Property(run => run.ModelVersion).HasMaxLength(100).IsRequired();
        builder.Property(run => run.InputFingerprint).HasMaxLength(64).IsRequired();
        builder.Property(run => run.SourceCutoffUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(run => run.OnHandQuantity).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(run => run.OnOrderQuantity).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(run => run.InTransitQuantity).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(run => run.SafetyStockQuantity).HasColumnType("decimal(28,12)");
        builder.Property(run => run.MeanAbsoluteError).HasColumnType("decimal(28,12)");
        builder.Property(run => run.MeanAbsolutePercentageError).HasColumnType("decimal(28,12)");
        builder.Property(run => run.Bias).HasColumnType("decimal(28,12)");
        builder.Property(run => run.BacktestScoresJson).HasMaxLength(8_000).IsRequired();
        builder.Property(run => run.DataQualityFlagsJson).HasMaxLength(4_000).IsRequired();
        builder.Property(run => run.BaseUnitOfMeasure).HasMaxLength(20).IsRequired();
        builder.Property(run => run.InsufficientDataReason).HasMaxLength(500);
        builder.Property(run => run.DaysOfSupply).HasColumnType("decimal(28,4)");
        builder.Property(run => run.RiskLevel).HasMaxLength(20).IsRequired();
        builder.Property(run => run.CreatedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasOne(run => run.Warehouse).WithMany().HasForeignKey(run => run.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(run => run.Item).WithMany().HasForeignKey(run => run.ItemId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(run => run.Points).WithOne(point => point.ForecastRun)
            .HasForeignKey(point => point.ForecastRunId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(run => run.Overrides).WithOne(overrideEntity => overrideEntity.ForecastRun)
            .HasForeignKey(overrideEntity => overrideEntity.ForecastRunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(run => new
        {
            run.WarehouseId,
            run.ItemId,
            run.Granularity,
            run.HorizonPeriods,
            run.InputFingerprint
        }).IsUnique();
        builder.HasIndex(run => new { run.WarehouseId, run.ItemId, run.CreatedAtUtc });
        builder.HasIndex(run => new { run.WarehouseId, run.Granularity, run.CreatedAtUtc });
    }
}

public sealed class ForecastRunPointConfiguration : IEntityTypeConfiguration<ForecastRunPointEntity>
{
    public void Configure(EntityTypeBuilder<ForecastRunPointEntity> builder)
    {
        builder.ToTable("ForecastRunPoints");
        builder.HasKey(point => point.Id);
        builder.Property(point => point.ActualDemand).HasColumnType("decimal(28,12)");
        builder.Property(point => point.ForecastQuantity).HasColumnType("decimal(28,12)");
        builder.Property(point => point.LowerBound).HasColumnType("decimal(28,12)");
        builder.Property(point => point.UpperBound).HasColumnType("decimal(28,12)");
        builder.HasIndex(point => new { point.ForecastRunId, point.PeriodStart }).IsUnique();
    }
}

public sealed class ForecastOverrideConfiguration : IEntityTypeConfiguration<ForecastOverrideEntity>
{
    public void Configure(EntityTypeBuilder<ForecastOverrideEntity> builder)
    {
        builder.ToTable("ForecastOverrides");
        builder.HasKey(overrideEntity => overrideEntity.Id);
        builder.Property(overrideEntity => overrideEntity.Quantity).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(overrideEntity => overrideEntity.Reason).HasMaxLength(1_000).IsRequired();
        builder.Property(overrideEntity => overrideEntity.CreatedByUserId).HasMaxLength(450).IsRequired();
        builder.Property(overrideEntity => overrideEntity.CreatedAtUtc)
            .HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(overrideEntity => new
        {
            overrideEntity.ForecastRunId,
            overrideEntity.PeriodStart,
            overrideEntity.Version
        }).IsUnique();
        builder.HasIndex(overrideEntity => new { overrideEntity.ForecastRunId, overrideEntity.PeriodStart });
    }
}
