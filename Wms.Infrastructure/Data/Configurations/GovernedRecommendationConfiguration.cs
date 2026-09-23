using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class GovernedRecommendationConfiguration : IEntityTypeConfiguration<GovernedRecommendation>
{
    public void Configure(EntityTypeBuilder<GovernedRecommendation> builder)
    {
        builder.ToTable("GovernedRecommendations");
        builder.HasKey(recommendation => recommendation.Id);
        builder.Property(recommendation => recommendation.RecommendationId).HasMaxLength(64).IsRequired();
        builder.Property(recommendation => recommendation.SourceSnapshotId).HasMaxLength(200).IsRequired();
        builder.Property(recommendation => recommendation.SourceStateFingerprint).HasMaxLength(128).IsRequired();
        builder.Property(recommendation => recommendation.ProviderName).HasMaxLength(100).IsRequired();
        builder.Property(recommendation => recommendation.ModelVersion).HasMaxLength(100).IsRequired();
        builder.Property(recommendation => recommendation.DeterministicBaselineVersion).HasMaxLength(100).IsRequired();
        builder.Property(recommendation => recommendation.Confidence).HasColumnType("decimal(9,6)").IsRequired();
        builder.Property(recommendation => recommendation.Explanation).HasMaxLength(2_000).IsRequired();
        builder.Property(recommendation => recommendation.ActionJson).HasMaxLength(2_000).IsRequired();
        builder.Property(recommendation => recommendation.NumericFeaturesJson).HasMaxLength(8_000).IsRequired();
        builder.Property(recommendation => recommendation.ExpectedImpactLow).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(recommendation => recommendation.ExpectedImpactHigh).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(recommendation => recommendation.GeneratedAtUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(recommendation => recommendation.ExpiresAtUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(recommendation => recommendation.SourceFromUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(recommendation => recommendation.SourceToUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(recommendation => recommendation.ShadowComparison).HasMaxLength(80);
        builder.Property(recommendation => recommendation.LastDispositionComment).HasMaxLength(1_000);
        builder.Property(recommendation => recommendation.ExecutionReference).HasMaxLength(250);
        builder.Property(recommendation => recommendation.LastExecutionErrorCode).HasMaxLength(100);
        builder.Property(recommendation => recommendation.CreatedByUserId).HasMaxLength(450).IsRequired();
        builder.Property(recommendation => recommendation.Revision).IsRequired().IsConcurrencyToken();
        builder.Property(recommendation => recommendation.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(recommendation => recommendation.UpdatedAt)
            .HasColumnType("timestamp with time zone");
        builder.HasIndex(recommendation => recommendation.RecommendationId).IsUnique();
        builder.HasIndex(recommendation => new
        {
            recommendation.WarehouseId,
            recommendation.Status,
            recommendation.GeneratedAtUtc
        });
        builder.HasIndex(recommendation => new
        {
            recommendation.Status,
            recommendation.ExpiresAtUtc
        });
    }
}

public sealed class GovernedRecommendationEventConfiguration : IEntityTypeConfiguration<GovernedRecommendationEvent>
{
    public void Configure(EntityTypeBuilder<GovernedRecommendationEvent> builder)
    {
        builder.ToTable("GovernedRecommendationEvents");
        builder.HasKey(recommendationEvent => recommendationEvent.Id);
        builder.Property(recommendationEvent => recommendationEvent.IdempotencyKey).HasMaxLength(250).IsRequired();
        builder.Property(recommendationEvent => recommendationEvent.EventType).HasMaxLength(40).IsRequired();
        builder.Property(recommendationEvent => recommendationEvent.ActorUserId).HasMaxLength(450).IsRequired();
        builder.Property(recommendationEvent => recommendationEvent.OccurredAtUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(recommendationEvent => recommendationEvent.Comment).HasMaxLength(1_000);
        builder.Property(recommendationEvent => recommendationEvent.StateFingerprint).HasMaxLength(128);
        builder.Property(recommendationEvent => recommendationEvent.CommandReference).HasMaxLength(250);
        builder.Property(recommendationEvent => recommendationEvent.OutcomeCode).HasMaxLength(100);
        builder.Property(recommendationEvent => recommendationEvent.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(recommendationEvent => recommendationEvent.UpdatedAt)
            .HasColumnType("timestamp with time zone");
        builder.HasOne(recommendationEvent => recommendationEvent.Recommendation)
            .WithMany()
            .HasForeignKey(recommendationEvent => recommendationEvent.RecommendationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(recommendationEvent => new
        {
            recommendationEvent.RecommendationId,
            recommendationEvent.IdempotencyKey
        }).IsUnique();
        builder.HasIndex(recommendationEvent => new
        {
            recommendationEvent.RecommendationId,
            recommendationEvent.OccurredAtUtc
        });
    }
}
