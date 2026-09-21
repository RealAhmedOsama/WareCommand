using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Infrastructure.Retention;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class WmsRetentionPolicyConfiguration : IEntityTypeConfiguration<WmsRetentionPolicyEntity>
{
    public void Configure(EntityTypeBuilder<WmsRetentionPolicyEntity> builder)
    {
        builder.ToTable("WmsRetentionPolicies");
        builder.HasKey(policy => policy.Id);
        builder.Property(policy => policy.Class).HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(policy => policy.PolicyKey).HasMaxLength(100).IsRequired();
        builder.Property(policy => policy.CompanyCode).HasMaxLength(50).IsRequired();
        builder.Property(policy => policy.CreatedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(policy => policy.UpdatedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(policy => new { policy.PolicyKey, policy.WarehouseId, policy.CompanyCode }).IsUnique();
        builder.HasIndex(policy => new { policy.Class, policy.WarehouseId, policy.CompanyCode });
    }
}

public sealed class WmsRetentionHoldConfiguration : IEntityTypeConfiguration<WmsRetentionHoldEntity>
{
    public void Configure(EntityTypeBuilder<WmsRetentionHoldEntity> builder)
    {
        builder.ToTable("WmsRetentionHolds");
        builder.HasKey(hold => hold.Id);
        builder.Property(hold => hold.Class).HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(hold => hold.TargetType).HasMaxLength(100).IsRequired();
        builder.Property(hold => hold.TargetId).HasMaxLength(200).IsRequired();
        builder.Property(hold => hold.Reason).HasMaxLength(2_000).IsRequired();
        builder.Property(hold => hold.CaseReference).HasMaxLength(200);
        builder.Property(hold => hold.CreatedByUserId).HasMaxLength(450).IsRequired();
        builder.Property(hold => hold.ReleasedByUserId).HasMaxLength(450);
        builder.Property(hold => hold.ReleaseReason).HasMaxLength(2_000);
        builder.Property(hold => hold.CreatedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(hold => hold.ReleasedAtUtc).HasColumnType("timestamp with time zone");
        builder.HasIndex(hold => new { hold.Class, hold.TargetType, hold.TargetId, hold.WarehouseId, hold.ReleasedAtUtc });
        builder.HasIndex(hold => hold.CaseReference);
    }
}

public sealed class WmsRetentionRunConfiguration : IEntityTypeConfiguration<WmsRetentionRunEntity>
{
    public void Configure(EntityTypeBuilder<WmsRetentionRunEntity> builder)
    {
        builder.ToTable("WmsRetentionRuns");
        builder.HasKey(run => run.RunId);
        builder.Property(run => run.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(run => run.AuthorizationReference).HasMaxLength(200);
        builder.Property(run => run.CompanyCode).HasMaxLength(50).IsRequired();
        builder.Property(run => run.CursorClass).HasMaxLength(50);
        builder.Property(run => run.CursorId).HasMaxLength(200);
        builder.Property(run => run.RequestedByUserId).HasMaxLength(450);
        builder.Property(run => run.LastError).HasMaxLength(2_000);
        builder.Property(run => run.AsOfUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(run => run.StartedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(run => run.CompletedAtUtc).HasColumnType("timestamp with time zone");
        builder.HasIndex(run => new { run.Status, run.StartedAtUtc });
    }
}

public sealed class WmsRetentionRunCountConfiguration : IEntityTypeConfiguration<WmsRetentionRunCountEntity>
{
    public void Configure(EntityTypeBuilder<WmsRetentionRunCountEntity> builder)
    {
        builder.ToTable("WmsRetentionRunCounts");
        builder.HasKey(count => new { count.RunId, count.Class });
        builder.Property(count => count.Class).HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.HasOne(count => count.Run)
            .WithMany(run => run.Counts)
            .HasForeignKey(count => count.RunId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class WmsRetentionArchiveReferenceConfiguration
    : IEntityTypeConfiguration<WmsRetentionArchiveReferenceEntity>
{
    public void Configure(EntityTypeBuilder<WmsRetentionArchiveReferenceEntity> builder)
    {
        builder.ToTable("WmsRetentionArchiveReferences");
        builder.HasKey(reference => reference.Id);
        builder.Property(reference => reference.Class).HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(reference => reference.SourceType).HasMaxLength(100).IsRequired();
        builder.Property(reference => reference.SourceId).HasMaxLength(200).IsRequired();
        builder.Property(reference => reference.ArchiveLocator).HasMaxLength(500).IsRequired();
        builder.Property(reference => reference.SourceHash).HasMaxLength(128);
        builder.Property(reference => reference.MetadataJson).HasMaxLength(8_000);
        builder.Property(reference => reference.ArchivedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(reference => reference.PurgedAtUtc).HasColumnType("timestamp with time zone");
        builder.HasIndex(reference => new { reference.Class, reference.SourceType, reference.SourceId }).IsUnique();
        builder.HasIndex(reference => new { reference.WarehouseId, reference.ArchivedAtUtc });
    }
}
