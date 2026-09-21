using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class ReasonCodeConfiguration : IEntityTypeConfiguration<ReasonCode>
{
    public void Configure(EntityTypeBuilder<ReasonCode> builder)
    {
        builder.ToTable("ReasonCodes");
        builder.HasKey(reason => reason.Id);
        builder.Property(reason => reason.Code).HasMaxLength(80).IsRequired();
        builder.Property(reason => reason.Module).HasMaxLength(100).IsRequired();
        builder.Property(reason => reason.Operation).HasMaxLength(150).IsRequired();
        builder.Property(reason => reason.NameEn).HasMaxLength(200).IsRequired();
        builder.Property(reason => reason.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(reason => reason.DescriptionEn).HasMaxLength(1_000);
        builder.Property(reason => reason.DescriptionAr).HasMaxLength(1_000);
        builder.Property(reason => reason.Category).HasConversion<int>().IsRequired();
        builder.Property(reason => reason.Severity).HasConversion<int>().IsRequired();
        builder.Property(reason => reason.EffectiveFromUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(reason => reason.EffectiveToUtc)
            .HasColumnType("timestamp with time zone");
        builder.Property(reason => reason.Revision).IsRequired().IsConcurrencyToken();
        builder.Property(reason => reason.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(reason => reason.UpdatedAt)
            .HasColumnType("timestamp with time zone");

        builder.HasOne<Warehouse>()
            .WithMany()
            .HasForeignKey(reason => reason.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(reason => new { reason.WarehouseId, reason.Code }).IsUnique();
        builder.HasIndex(reason => new { reason.Module, reason.Operation, reason.IsActive });
        builder.HasIndex(reason => new { reason.WarehouseId, reason.IsActive, reason.EffectiveFromUtc });
    }
}
