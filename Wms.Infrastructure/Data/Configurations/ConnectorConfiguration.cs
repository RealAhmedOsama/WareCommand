using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Infrastructure.Connectors;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class WmsConnectorMappingProfileConfiguration
    : IEntityTypeConfiguration<WmsConnectorMappingProfileEntity>
{
    public void Configure(EntityTypeBuilder<WmsConnectorMappingProfileEntity> builder)
    {
        builder.ToTable("WmsConnectorMappingProfiles");
        builder.HasKey(profile => profile.Id);
        builder.Property(profile => profile.ConnectorType).HasMaxLength(100).IsRequired();
        builder.Property(profile => profile.Name).HasMaxLength(200).IsRequired();
        builder.Property(profile => profile.ExternalIdField).HasMaxLength(200).IsRequired();
        builder.Property(profile => profile.RulesJson).HasMaxLength(200_000).IsRequired();
        builder.Property(profile => profile.CultureName).HasMaxLength(80).IsRequired();
        builder.Property(profile => profile.ConflictPolicy).HasMaxLength(40).IsRequired();
        builder.Property(profile => profile.CreatedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(profile => profile.UpdatedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(profile => new { profile.ConnectorType, profile.Name, profile.Version }).IsUnique();
        builder.HasIndex(profile => new { profile.ConnectorType, profile.IsActive });
    }
}

public sealed class WmsConnectorInstanceConfiguration
    : IEntityTypeConfiguration<WmsConnectorInstanceEntity>
{
    public void Configure(EntityTypeBuilder<WmsConnectorInstanceEntity> builder)
    {
        builder.ToTable("WmsConnectorInstances");
        builder.HasKey(instance => instance.Id);
        builder.Property(instance => instance.ConnectorType).HasMaxLength(100).IsRequired();
        builder.Property(instance => instance.Name).HasMaxLength(200).IsRequired();
        builder.Property(instance => instance.AllowedWarehouseIdsJson).HasMaxLength(4_000).IsRequired();
        builder.Property(instance => instance.CredentialReference).HasMaxLength(250).IsRequired();
        builder.Property(instance => instance.ModesJson).HasMaxLength(1_000).IsRequired();
        builder.Property(instance => instance.Schedule).HasMaxLength(120);
        builder.Property(instance => instance.MappingProfileName).HasMaxLength(200).IsRequired();
        builder.Property(instance => instance.Status).HasMaxLength(30).IsRequired();
        builder.Property(instance => instance.Cursor).HasMaxLength(1_000).IsRequired();
        builder.Property(instance => instance.HealthStatus).HasMaxLength(30).IsRequired();
        builder.Property(instance => instance.HealthSummary).HasMaxLength(2_000);
        builder.Property(instance => instance.CreatedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(instance => instance.UpdatedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(instance => instance.LastRunAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(instance => instance.LastSuccessfulRunAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(instance => instance.LastHealthCheckAtUtc).HasColumnType("timestamp with time zone");
        builder.HasIndex(instance => new { instance.ConnectorType, instance.Name }).IsUnique();
        builder.HasIndex(instance => new { instance.Status, instance.HealthStatus });
        builder.HasOne(instance => instance.MappingProfile)
            .WithMany(profile => profile.ConnectorInstances)
            .HasPrincipalKey(profile => new { profile.ConnectorType, profile.Name, profile.Version })
            .HasForeignKey(instance => new
            {
                instance.ConnectorType,
                instance.MappingProfileName,
                instance.MappingProfileVersion
            })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class WmsConnectorRunConfiguration : IEntityTypeConfiguration<WmsConnectorRunEntity>
{
    public void Configure(EntityTypeBuilder<WmsConnectorRunEntity> builder)
    {
        builder.ToTable("WmsConnectorRuns");
        builder.HasKey(run => run.Id);
        builder.Property(run => run.Operation).HasMaxLength(80).IsRequired();
        builder.Property(run => run.Mode).HasMaxLength(30).IsRequired();
        builder.Property(run => run.Status).HasMaxLength(30).IsRequired();
        builder.Property(run => run.IdempotencyKey).HasMaxLength(250).IsRequired();
        builder.Property(run => run.CursorBefore).HasMaxLength(1_000);
        builder.Property(run => run.CursorAfter).HasMaxLength(1_000);
        builder.Property(run => run.CorrelationId).HasMaxLength(100).IsRequired();
        builder.Property(run => run.ErrorCode).HasMaxLength(150);
        builder.Property(run => run.ErrorMessage).HasMaxLength(2_000);
        builder.Property(run => run.CreatedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(run => run.StartedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(run => run.CompletedAtUtc).HasColumnType("timestamp with time zone");
        builder.HasOne(run => run.ConnectorInstance)
            .WithMany(instance => instance.Runs)
            .HasForeignKey(run => run.ConnectorInstanceId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(run => new { run.ConnectorInstanceId, run.IdempotencyKey }).IsUnique();
        builder.HasIndex(run => new { run.ConnectorInstanceId, run.CreatedAtUtc });
        builder.HasIndex(run => new { run.Status, run.CreatedAtUtc });
    }
}

public sealed class WmsConnectorExternalRecordConfiguration
    : IEntityTypeConfiguration<WmsConnectorExternalRecordEntity>
{
    public void Configure(EntityTypeBuilder<WmsConnectorExternalRecordEntity> builder)
    {
        builder.ToTable("WmsConnectorExternalRecords");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.ExternalKey).HasMaxLength(64).IsRequired();
        builder.Property(record => record.RecordType).HasMaxLength(100).IsRequired();
        builder.Property(record => record.ExternalId).HasMaxLength(250).IsRequired();
        builder.Property(record => record.PayloadHash).HasMaxLength(64).IsRequired();
        builder.Property(record => record.ExternalVersion).HasMaxLength(200);
        builder.Property(record => record.Status).HasMaxLength(30).IsRequired();
        builder.Property(record => record.FirstSeenAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(record => record.LastSeenAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasOne(record => record.ConnectorInstance)
            .WithMany(instance => instance.ExternalRecords)
            .HasForeignKey(record => record.ConnectorInstanceId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(record => new { record.ConnectorInstanceId, record.ExternalKey }).IsUnique();
        builder.HasIndex(record => new { record.ConnectorInstanceId, record.RecordType, record.ExternalId }).IsUnique();
        builder.HasIndex(record => new { record.Status, record.LastSeenAtUtc });
    }
}
