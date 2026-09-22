using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Infrastructure.BulkExchange;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class WmsBulkImportMappingProfileConfiguration
    : IEntityTypeConfiguration<WmsBulkImportMappingProfileEntity>
{
    public void Configure(EntityTypeBuilder<WmsBulkImportMappingProfileEntity> builder)
    {
        builder.ToTable("WmsBulkImportMappingProfiles");
        builder.HasKey(profile => profile.Id);
        builder.Property(profile => profile.ImportType).HasMaxLength(100).IsRequired();
        builder.Property(profile => profile.Name).HasMaxLength(200).IsRequired();
        builder.Property(profile => profile.ColumnsJson).HasMaxLength(50_000).IsRequired();
        builder.Property(profile => profile.CultureName).HasMaxLength(50).IsRequired();
        builder.Property(profile => profile.TimeZone).HasMaxLength(100).IsRequired();
        builder.Property(profile => profile.DuplicatePolicy).HasMaxLength(30).IsRequired();
        builder.Property(profile => profile.CreatedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(profile => profile.UpdatedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(profile => new { profile.ImportType, profile.Name, profile.Version }).IsUnique();
        builder.HasIndex(profile => new { profile.ImportType, profile.IsActive });
    }
}

public sealed class WmsBulkImportExecutionConfiguration
    : IEntityTypeConfiguration<WmsBulkImportExecutionEntity>
{
    public void Configure(EntityTypeBuilder<WmsBulkImportExecutionEntity> builder)
    {
        builder.ToTable("WmsBulkImportExecutions");
        builder.HasKey(execution => execution.Id);
        builder.Property(execution => execution.ImportType).HasMaxLength(100).IsRequired();
        builder.Property(execution => execution.Format).HasMaxLength(20).IsRequired();
        builder.Property(execution => execution.Mode).HasMaxLength(20).IsRequired();
        builder.Property(execution => execution.Status).HasMaxLength(30).IsRequired();
        builder.Property(execution => execution.MappingName).HasMaxLength(200).IsRequired();
        builder.Property(execution => execution.CultureName).HasMaxLength(50).IsRequired();
        builder.Property(execution => execution.TimeZone).HasMaxLength(100).IsRequired();
        builder.Property(execution => execution.DuplicatePolicy).HasMaxLength(30).IsRequired();
        builder.Property(execution => execution.UserId).HasMaxLength(450).IsRequired();
        builder.Property(execution => execution.IdempotencyKey).HasMaxLength(250);
        builder.Property(execution => execution.CorrelationId).HasMaxLength(100).IsRequired();
        builder.Property(execution => execution.Summary).HasMaxLength(2_000);
        builder.Property(execution => execution.CreatedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(execution => execution.StartedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(execution => execution.CompletedAtUtc).HasColumnType("timestamp with time zone");
        builder.HasOne(execution => execution.SourceFile)
            .WithOne(source => source.Execution)
            .HasForeignKey<WmsBulkImportExecutionEntity>(execution => execution.SourceFileId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(execution => new { execution.ImportType, execution.Status, execution.CreatedAtUtc });
        builder.HasIndex(execution => new { execution.UserId, execution.IdempotencyKey }).IsUnique();
    }
}

public sealed class WmsBulkImportSourceFileConfiguration
    : IEntityTypeConfiguration<WmsBulkImportSourceFileEntity>
{
    public void Configure(EntityTypeBuilder<WmsBulkImportSourceFileEntity> builder)
    {
        builder.ToTable("WmsBulkImportSourceFiles");
        builder.HasKey(source => source.Id);
        builder.Property(source => source.FileName).HasMaxLength(260).IsRequired();
        builder.Property(source => source.ContentType).HasMaxLength(200).IsRequired();
        builder.Property(source => source.Format).HasMaxLength(20).IsRequired();
        builder.Property(source => source.Sha256).HasMaxLength(64).IsRequired();
        builder.Property(source => source.Content).HasMaxLength(5_000_000).IsRequired();
        builder.Property(source => source.CreatedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(source => source.Sha256);
    }
}

public sealed class WmsBulkImportRowResultConfiguration
    : IEntityTypeConfiguration<WmsBulkImportRowResultEntity>
{
    public void Configure(EntityTypeBuilder<WmsBulkImportRowResultEntity> builder)
    {
        builder.ToTable("WmsBulkImportRows");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Status).HasMaxLength(30).IsRequired();
        builder.Property(row => row.ValuesJson).HasMaxLength(100_000).IsRequired();
        builder.Property(row => row.ValidationErrorsJson).HasMaxLength(100_000).IsRequired();
        builder.Property(row => row.OutputReference).HasMaxLength(250);
        builder.Property(row => row.ProcessedAtUtc).HasColumnType("timestamp with time zone");
        builder.HasOne(row => row.Execution)
            .WithMany(execution => execution.Rows)
            .HasForeignKey(row => row.ExecutionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(row => new { row.ExecutionId, row.RowNumber }).IsUnique();
        builder.HasIndex(row => new { row.ExecutionId, row.Status });
    }
}
