using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Infrastructure.Labels;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class WmsLabelTemplateConfiguration : IEntityTypeConfiguration<WmsLabelTemplateEntity>
{
    public void Configure(EntityTypeBuilder<WmsLabelTemplateEntity> builder)
    {
        builder.ToTable("WmsLabelTemplates");
        builder.HasKey(template => template.Id);
        builder.Property(template => template.Name).HasMaxLength(100).IsRequired();
        builder.Property(template => template.Language).HasMaxLength(20).IsRequired();
        builder.Property(template => template.WidthMillimeters).HasColumnType("numeric(10,2)").IsRequired();
        builder.Property(template => template.HeightMillimeters).HasColumnType("numeric(10,2)").IsRequired();
        builder.Property(template => template.Body).HasMaxLength(50_000).IsRequired();
        builder.Property(template => template.FieldsJson).HasMaxLength(100_000).IsRequired();
        builder.Property(template => template.BarcodesJson).HasMaxLength(50_000).IsRequired();
        builder.Property(template => template.RoutesJson).HasMaxLength(50_000).IsRequired();
        builder.Property(template => template.CustomerCode).HasMaxLength(100);
        builder.Property(template => template.SupplierCode).HasMaxLength(100);
        builder.Property(template => template.ContentHash).HasMaxLength(64).IsRequired();
        builder.Property(template => template.CreatedByUserId).HasMaxLength(450).IsRequired();
        builder.Property(template => template.ActivatedByUserId).HasMaxLength(450);
        builder.Property(template => template.CreatedAtUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(template => template.ActivatedAtUtc)
            .HasColumnType("timestamp with time zone");
        builder.HasIndex(template => new
        {
            template.Name,
            template.Version,
            template.Language,
            template.WarehouseId,
            template.CustomerCode,
            template.SupplierCode
        }).IsUnique();
        builder.HasIndex(template => new
        {
            template.Name,
            template.DocumentType,
            template.Language,
            template.Format,
            template.IsActive
        });
    }
}

public sealed class WmsPrintJobConfiguration : IEntityTypeConfiguration<WmsPrintJobEntity>
{
    public void Configure(EntityTypeBuilder<WmsPrintJobEntity> builder)
    {
        builder.ToTable("WmsPrintJobs");
        builder.HasKey(job => job.Id);
        builder.Property(job => job.IdempotencyKey).HasMaxLength(250).IsRequired();
        builder.Property(job => job.Status).HasMaxLength(30).IsRequired();
        builder.Property(job => job.TemplateName).HasMaxLength(100).IsRequired();
        builder.Property(job => job.Language).HasMaxLength(20).IsRequired();
        builder.Property(job => job.StationCode).HasMaxLength(100);
        builder.Property(job => job.RouteName).HasMaxLength(100);
        builder.Property(job => job.AdapterKey).HasMaxLength(150);
        builder.Property(job => job.ReprintReason).HasMaxLength(500);
        builder.Property(job => job.SourceReference).HasMaxLength(200);
        builder.Property(job => job.ActorUserId).HasMaxLength(450).IsRequired();
        builder.Property(job => job.ActorUserName).HasMaxLength(256);
        builder.Property(job => job.DataJson).HasMaxLength(200_000).IsRequired();
        builder.Property(job => job.Payload).IsRequired();
        builder.Property(job => job.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(job => job.TextPreview).HasMaxLength(50_000).IsRequired();
        builder.Property(job => job.BrowserHtml).HasMaxLength(200_000).IsRequired();
        builder.Property(job => job.ErrorCode).HasMaxLength(150);
        builder.Property(job => job.ErrorMessage).HasMaxLength(2_000);
        builder.Property(job => job.CreatedAtUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(job => job.LastAttemptAtUtc)
            .HasColumnType("timestamp with time zone");
        builder.Property(job => job.CompletedAtUtc)
            .HasColumnType("timestamp with time zone");
        builder.HasIndex(job => job.IdempotencyKey).IsUnique();
        builder.HasIndex(job => new { job.Status, job.CreatedAtUtc });
        builder.HasIndex(job => new { job.TemplateName, job.TemplateVersion, job.CreatedAtUtc });
    }
}
