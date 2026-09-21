using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class CrossDockPlanLineConfiguration : IEntityTypeConfiguration<CrossDockPlanLine>
{
    public void Configure(EntityTypeBuilder<CrossDockPlanLine> builder)
    {
        builder.ToTable("CrossDockPlanLines");
        builder.HasKey(line => line.Id);
        builder.Property(line => line.Status).HasConversion<int>().IsRequired();
        builder.Property(line => line.SalesOrderDocumentNumber).HasMaxLength(50).IsRequired();
        builder.Property(line => line.ItemSku).HasMaxLength(50).IsRequired();
        builder.Property(line => line.Reason).HasMaxLength(1_000);
        builder.Property(line => line.DemandBaseQuantity).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(line => line.MatchedBaseQuantity).HasColumnType("decimal(28,12)").IsRequired();
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
        builder.HasOne(line => line.Customer)
            .WithMany()
            .HasForeignKey(line => line.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(line => new { line.CrossDockPlanId, line.Sequence }).IsUnique();
        builder.HasIndex(line => new { line.SalesOrderLineId, line.Status });
    }
}
