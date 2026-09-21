using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class ReturnLineConfiguration : IEntityTypeConfiguration<ReturnLine>
{
    public void Configure(EntityTypeBuilder<ReturnLine> builder)
    {
        builder.ToTable("ReturnLines");
        builder.HasKey(value => value.Id);
        builder.Property(value => value.ExpectedQuantity).HasColumnType("numeric(28,12)");
        builder.Property(value => value.ReceivedQuantity).HasColumnType("numeric(28,12)");
        builder.Property(value => value.DisposedQuantity).HasColumnType("numeric(28,12)");
        builder.Property(value => value.Revision).IsConcurrencyToken();
        builder.HasOne(value => value.ReturnAuthorization).WithMany(value => value.Lines).HasForeignKey(value => value.ReturnAuthorizationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(value => value.Item).WithMany().HasForeignKey(value => value.ItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.SalesOrderLine).WithMany().HasForeignKey(value => value.SalesOrderLineId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.ShipmentLine).WithMany().HasForeignKey(value => value.ShipmentLineId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new { value.ReturnAuthorizationId, value.ItemId, value.SalesOrderLineId }).IsUnique();
    }
}
