using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class ShipmentLineConfiguration : IEntityTypeConfiguration<ShipmentLine>
{
    public void Configure(EntityTypeBuilder<ShipmentLine> builder)
    {
        builder.ToTable("ShipmentLines");
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Quantity).HasColumnType("numeric(28,12)");
        builder.Property(value => value.BaseUnitOfMeasure).HasMaxLength(20).IsRequired();
        builder.Property(value => value.Revision).IsConcurrencyToken();
        builder.HasOne(value => value.Shipment).WithMany(value => value.Lines).HasForeignKey(value => value.ShipmentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(value => value.SalesOrderLine).WithMany().HasForeignKey(value => value.SalesOrderLineId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.Item).WithMany().HasForeignKey(value => value.ItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new { value.ShipmentId, value.SalesOrderLineId, value.ItemId }).IsUnique();
    }
}
