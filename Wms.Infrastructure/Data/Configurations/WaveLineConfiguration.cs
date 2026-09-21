using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class WaveLineConfiguration : IEntityTypeConfiguration<WaveLine>
{
    public void Configure(EntityTypeBuilder<WaveLine> builder)
    {
        builder.ToTable("WaveLines");
        builder.HasKey(line => line.Id);
        builder.Property(line => line.ItemSkuSnapshot).HasMaxLength(50).IsRequired();
        builder.Property(line => line.RequestedQuantity).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(line => line.AllocatedQuantity).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(line => line.BackorderQuantity).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(line => line.Status).HasConversion<int>().IsRequired();
        builder.Property(line => line.ErrorCode).HasMaxLength(120);
        builder.Property(line => line.ErrorMessage).HasMaxLength(2_000);
        builder.Property(line => line.Explanation).HasMaxLength(2_000);
        builder.Property(line => line.ProcessedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(line => line.Revision).IsRequired().IsConcurrencyToken();
        builder.Property(line => line.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(line => line.UpdatedAt).HasColumnType("timestamp with time zone");

        builder.HasOne(line => line.SalesOrder)
            .WithMany()
            .HasForeignKey(line => line.SalesOrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(line => line.SalesOrderLine)
            .WithMany()
            .HasForeignKey(line => line.SalesOrderLineId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(line => line.Item)
            .WithMany()
            .HasForeignKey(line => line.ItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(line => new { line.WaveId, line.SalesOrderLineId }).IsUnique();
        builder.HasIndex(line => new { line.SalesOrderLineId, line.Status });
        builder.HasIndex(line => new { line.SalesOrderId, line.Status });
    }
}
