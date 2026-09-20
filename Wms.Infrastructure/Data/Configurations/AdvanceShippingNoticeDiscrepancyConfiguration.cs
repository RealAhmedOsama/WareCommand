using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class AdvanceShippingNoticeDiscrepancyConfiguration
    : IEntityTypeConfiguration<AdvanceShippingNoticeDiscrepancy>
{
    public void Configure(EntityTypeBuilder<AdvanceShippingNoticeDiscrepancy> builder)
    {
        builder.ToTable("AdvanceShippingNoticeDiscrepancies");
        builder.HasKey(discrepancy => discrepancy.Id);

        builder.Property(discrepancy => discrepancy.Kind)
            .HasConversion<int>()
            .IsRequired();
        builder.Property(discrepancy => discrepancy.ExpectedBaseQuantity).HasColumnType("decimal(28,12)");
        builder.Property(discrepancy => discrepancy.ReceivedBaseQuantity).HasColumnType("decimal(28,12)");
        builder.Property(discrepancy => discrepancy.VarianceBaseQuantity).HasColumnType("decimal(28,12)");
        builder.Property(discrepancy => discrepancy.Details).IsRequired().HasMaxLength(2_000);
        builder.Property(discrepancy => discrepancy.RecordedByUserId).IsRequired().HasMaxLength(450);
        builder.Property(discrepancy => discrepancy.OccurredAtUtc).IsRequired().HasColumnType("timestamp with time zone");
        builder.Property(discrepancy => discrepancy.CreatedAt).IsRequired().HasColumnType("timestamp with time zone");
        builder.Property(discrepancy => discrepancy.UpdatedAt).HasColumnType("timestamp with time zone");

        builder.HasOne(discrepancy => discrepancy.AdvanceShippingNotice)
            .WithMany()
            .HasForeignKey(discrepancy => discrepancy.AdvanceShippingNoticeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(discrepancy => discrepancy.AdvanceShippingNoticeLine)
            .WithMany()
            .HasForeignKey(discrepancy => discrepancy.AdvanceShippingNoticeLineId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(discrepancy => new { discrepancy.AdvanceShippingNoticeId, discrepancy.OccurredAtUtc });
        builder.HasIndex(discrepancy => discrepancy.AdvanceShippingNoticeLineId);
    }
}
