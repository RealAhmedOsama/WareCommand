using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class WaveTemplateConfiguration : IEntityTypeConfiguration<WaveTemplate>
{
    public void Configure(EntityTypeBuilder<WaveTemplate> builder)
    {
        builder.ToTable("WaveTemplates");
        builder.HasKey(template => template.Id);
        builder.Property(template => template.TemplateKey).HasMaxLength(80).IsRequired();
        builder.Property(template => template.Name).HasMaxLength(200).IsRequired();
        builder.Property(template => template.TriggerType).HasConversion<int>().IsRequired();
        builder.Property(template => template.Priority).IsRequired();
        builder.Property(template => template.Limit).IsRequired();
        builder.Property(template => template.ReleaseToWarehouse).IsRequired();
        builder.Property(template => template.ScheduleCron).HasMaxLength(100);
        builder.Property(template => template.CarrierCode).HasMaxLength(50);
        builder.Property(template => template.SourceType).HasMaxLength(30);
        builder.Property(template => template.Revision).IsRequired().IsConcurrencyToken();
        builder.Property(template => template.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(template => template.UpdatedAt).HasColumnType("timestamp with time zone");

        builder.HasOne(template => template.Warehouse)
            .WithMany()
            .HasForeignKey(template => template.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(template => template.Customer)
            .WithMany()
            .HasForeignKey(template => template.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(template => new { template.WarehouseId, template.TemplateKey }).IsUnique();
        builder.HasIndex(template => new { template.WarehouseId, template.IsActive, template.TriggerType });
    }
}
