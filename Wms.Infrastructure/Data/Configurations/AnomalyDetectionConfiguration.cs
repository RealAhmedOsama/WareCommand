using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Infrastructure.AnomalyDetection;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class AnomalyRuleConfigurationEntityConfiguration
    : IEntityTypeConfiguration<AnomalyRuleConfigurationEntity>
{
    public void Configure(EntityTypeBuilder<AnomalyRuleConfigurationEntity> builder)
    {
        builder.ToTable("AnomalyRuleConfigurations", table =>
        {
            table.HasCheckConstraint("CK_AnomalyRuleConfigurations_Version", "\"Version\" > 0");
            table.HasCheckConstraint("CK_AnomalyRuleConfigurations_Threshold", "\"Threshold\" >= 0");
        });
        builder.HasKey(rule => rule.Id);
        builder.Property(rule => rule.RuleKind).HasMaxLength(40).IsRequired();
        builder.Property(rule => rule.ScopeKey).HasMaxLength(64).IsRequired();
        builder.Property(rule => rule.Threshold).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(rule => rule.CreatedByUserId).HasMaxLength(450).IsRequired();
        builder.Property(rule => rule.CreatedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasOne(rule => rule.Warehouse).WithMany().HasForeignKey(rule => rule.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(rule => new { rule.RuleKind, rule.ScopeKey, rule.Version }).IsUnique();
        builder.HasIndex(rule => new { rule.WarehouseId, rule.RuleKind, rule.Version });
    }
}

public sealed class AnomalyDetectionRunEntityConfiguration
    : IEntityTypeConfiguration<AnomalyDetectionRunEntity>
{
    public void Configure(EntityTypeBuilder<AnomalyDetectionRunEntity> builder)
    {
        builder.ToTable("AnomalyDetectionRuns", table =>
            table.HasCheckConstraint(
                "CK_AnomalyDetectionRuns_Window",
                "\"SourceWindowToUtc\" >= \"SourceWindowFromUtc\""));
        builder.HasKey(run => run.Id);
        builder.Property(run => run.InputFingerprint).HasMaxLength(64).IsRequired();
        builder.Property(run => run.RuleVersionsJson).HasMaxLength(4_000).IsRequired();
        builder.Property(run => run.DataQualityFlagsJson).HasMaxLength(4_000).IsRequired();
        builder.Property(run => run.StartedByUserId).HasMaxLength(450);
        builder.Property(run => run.SourceWindowFromUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(run => run.SourceWindowToUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(run => run.StartedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(run => run.CompletedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasOne(run => run.Warehouse).WithMany().HasForeignKey(run => run.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(run => run.Observations).WithOne(observation => observation.Run)
            .HasForeignKey(observation => observation.RunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(run => new { run.WarehouseId, run.InputFingerprint }).IsUnique();
        builder.HasIndex(run => new { run.WarehouseId, run.CompletedAtUtc });
    }
}

public sealed class AnomalyFindingEntityConfiguration : IEntityTypeConfiguration<AnomalyFindingEntity>
{
    public void Configure(EntityTypeBuilder<AnomalyFindingEntity> builder)
    {
        builder.ToTable("AnomalyFindings", table =>
            table.HasCheckConstraint("CK_AnomalyFindings_Revision", "\"Revision\" > 0"));
        builder.HasKey(finding => finding.Id);
        builder.Property(finding => finding.Fingerprint).HasMaxLength(64).IsRequired();
        builder.Property(finding => finding.RuleKind).HasMaxLength(40).IsRequired();
        builder.Property(finding => finding.Severity).HasMaxLength(20).IsRequired();
        builder.Property(finding => finding.Status).HasMaxLength(30).IsRequired();
        builder.Property(finding => finding.RuleVersion).HasMaxLength(100).IsRequired();
        builder.Property(finding => finding.SourceType).HasMaxLength(100).IsRequired();
        builder.Property(finding => finding.SourceId).HasMaxLength(200).IsRequired();
        builder.Property(finding => finding.ObservedValue).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(finding => finding.ExpectedValue).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(finding => finding.Threshold).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(finding => finding.Explanation).HasMaxLength(500).IsRequired();
        builder.Property(finding => finding.SourceWindowFromUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(finding => finding.SourceWindowToUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(finding => finding.ObservedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(finding => finding.FirstDetectedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(finding => finding.AssignedToUserId).HasMaxLength(450);
        builder.Property(finding => finding.AssignedTeamCode).HasMaxLength(50);
        builder.Property(finding => finding.AssignedByUserId).HasMaxLength(450);
        builder.Property(finding => finding.AssignedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(finding => finding.SuppressionExpiresAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(finding => finding.SuppressedFromStatus).HasMaxLength(30);
        builder.Property(finding => finding.Revision).IsConcurrencyToken();
        builder.HasOne(finding => finding.Warehouse).WithMany().HasForeignKey(finding => finding.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(finding => finding.FirstDetectionRun).WithMany().HasForeignKey(finding => finding.FirstDetectionRunId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(finding => finding.Observations).WithOne(observation => observation.Finding)
            .HasForeignKey(observation => observation.FindingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(finding => finding.History).WithOne(history => history.Finding)
            .HasForeignKey(history => history.FindingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(finding => finding.Fingerprint).IsUnique();
        builder.HasIndex(finding => new { finding.WarehouseId, finding.Status, finding.FirstDetectedAtUtc });
        builder.HasIndex(finding => new { finding.WarehouseId, finding.RuleKind, finding.Status });
    }
}

public sealed class AnomalyFindingObservationEntityConfiguration
    : IEntityTypeConfiguration<AnomalyFindingObservationEntity>
{
    public void Configure(EntityTypeBuilder<AnomalyFindingObservationEntity> builder)
    {
        builder.ToTable("AnomalyFindingObservations", table =>
            table.HasCheckConstraint(
                "CK_AnomalyFindingObservations_Window",
                "\"SourceWindowToUtc\" >= \"SourceWindowFromUtc\""));
        builder.HasKey(observation => observation.Id);
        builder.Property(observation => observation.SourceWindowFromUtc)
            .HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(observation => observation.SourceWindowToUtc)
            .HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(observation => observation.ObservedValue).HasColumnType("decimal(28,12)");
        builder.Property(observation => observation.ExpectedValue).HasColumnType("decimal(28,12)");
        builder.Property(observation => observation.Threshold).HasColumnType("decimal(28,12)");
        builder.Property(observation => observation.Explanation).HasMaxLength(500);
        builder.Property(observation => observation.ObservedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(observation => observation.RecordedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasOne(observation => observation.Run).WithMany(run => run.Observations)
            .HasForeignKey(observation => observation.RunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(observation => observation.Finding).WithMany(finding => finding.Observations)
            .HasForeignKey(observation => observation.FindingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(observation => new { observation.RunId, observation.FindingId }).IsUnique();
        builder.HasIndex(observation => new { observation.FindingId, observation.RecordedAtUtc });
    }
}

public sealed class AnomalyFindingHistoryEntityConfiguration
    : IEntityTypeConfiguration<AnomalyFindingHistoryEntity>
{
    public void Configure(EntityTypeBuilder<AnomalyFindingHistoryEntity> builder)
    {
        builder.ToTable("AnomalyFindingHistory", table =>
        {
            table.HasCheckConstraint("CK_AnomalyFindingHistory_Sequence", "\"Sequence\" > 0");
        });
        builder.HasKey(history => history.Id);
        builder.Property(history => history.Action).HasMaxLength(40).IsRequired();
        builder.Property(history => history.FromStatus).HasMaxLength(30);
        builder.Property(history => history.ToStatus).HasMaxLength(30);
        builder.Property(history => history.AssignedToUserId).HasMaxLength(450);
        builder.Property(history => history.AssignedTeamCode).HasMaxLength(50);
        builder.Property(history => history.Comment).HasMaxLength(1_000);
        builder.Property(history => history.EvidenceReferencesJson).HasMaxLength(6_000).IsRequired();
        builder.Property(history => history.SuppressionExpiresAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(history => history.ActorUserId).HasMaxLength(450).IsRequired();
        builder.Property(history => history.CreatedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasOne(history => history.Finding).WithMany(finding => finding.History)
            .HasForeignKey(history => history.FindingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(history => new { history.FindingId, history.Sequence }).IsUnique();
    }
}
