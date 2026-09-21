using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class SlottingRecommendationConfiguration : IEntityTypeConfiguration<SlottingRecommendation>
{
    public void Configure(EntityTypeBuilder<SlottingRecommendation> builder)
    {
        builder.ToTable("SlottingRecommendations");
        builder.HasKey(recommendation => recommendation.Id);
        builder.Property(recommendation => recommendation.RecommendationKey).HasMaxLength(300).IsRequired();
        builder.Property(recommendation => recommendation.Kind).HasConversion<int>().IsRequired();
        builder.Property(recommendation => recommendation.Quantity).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(recommendation => recommendation.BaseUnitOfMeasure).HasMaxLength(20).IsRequired();
        builder.Property(recommendation => recommendation.Score).HasColumnType("decimal(18,8)").IsRequired();
        builder.Property(recommendation => recommendation.CurrentScore).HasColumnType("decimal(18,8)").IsRequired();
        builder.Property(recommendation => recommendation.ExpectedTravelReduction).HasColumnType("decimal(18,8)").IsRequired();
        builder.Property(recommendation => recommendation.ExpectedReplenishmentReduction).HasColumnType("decimal(18,8)").IsRequired();
        builder.Property(recommendation => recommendation.ExpectedCongestionReduction).HasColumnType("decimal(18,8)").IsRequired();
        builder.Property(recommendation => recommendation.SourcePeriodFromUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(recommendation => recommendation.SourcePeriodToUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(recommendation => recommendation.SourceDataVersion).HasMaxLength(80).IsRequired();
        builder.Property(recommendation => recommendation.FactorSnapshotJson).HasMaxLength(8_000).IsRequired();
        builder.Property(recommendation => recommendation.ConstraintSnapshotJson).HasMaxLength(8_000).IsRequired();
        builder.Property(recommendation => recommendation.AnalysisRunKey).HasMaxLength(250).IsRequired();
        builder.Property(recommendation => recommendation.ExpiresAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(recommendation => recommendation.Status).HasConversion<int>().IsRequired();
        builder.Property(recommendation => recommendation.ApprovedByUserId).HasMaxLength(450);
        builder.Property(recommendation => recommendation.ApprovedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(recommendation => recommendation.RejectedByUserId).HasMaxLength(450);
        builder.Property(recommendation => recommendation.RejectedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(recommendation => recommendation.RejectionReason).HasMaxLength(1_000);
        builder.Property(recommendation => recommendation.WorkCreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(recommendation => recommendation.Revision).IsRequired().IsConcurrencyToken();
        builder.Property(recommendation => recommendation.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(recommendation => recommendation.UpdatedAt).HasColumnType("timestamp with time zone");

        builder.HasOne(recommendation => recommendation.Warehouse)
            .WithMany()
            .HasForeignKey(recommendation => recommendation.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(recommendation => recommendation.Item)
            .WithMany()
            .HasForeignKey(recommendation => recommendation.ItemId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(recommendation => recommendation.SourceLocation)
            .WithMany()
            .HasForeignKey(recommendation => new { recommendation.WarehouseId, recommendation.SourceLocationId })
            .HasPrincipalKey(location => new { location.WarehouseId, location.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(recommendation => recommendation.TargetLocation)
            .WithMany()
            .HasForeignKey(recommendation => new { recommendation.WarehouseId, recommendation.TargetLocationId })
            .HasPrincipalKey(location => new { location.WarehouseId, location.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(recommendation => recommendation.Work)
            .WithMany()
            .HasForeignKey(recommendation => recommendation.WorkId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(recommendation => recommendation.RecommendationKey).IsUnique();
        builder.HasIndex(recommendation => new
        {
            recommendation.WarehouseId,
            recommendation.Status,
            recommendation.ExpiresAtUtc,
            recommendation.ItemId
        });
        builder.HasIndex(recommendation => new
        {
            recommendation.WarehouseId,
            recommendation.AnalysisRunKey
        });
    }
}
