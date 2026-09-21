using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class ReturnReceiptConfiguration : IEntityTypeConfiguration<ReturnReceipt>
{
    public void Configure(EntityTypeBuilder<ReturnReceipt> builder)
    {
        builder.ToTable("ReturnReceipts");
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Quantity).HasColumnType("numeric(28,12)");
        builder.Property(value => value.DisposedQuantity).HasColumnType("numeric(28,12)");
        builder.Property(value => value.SerialNumber).HasMaxLength(100);
        builder.Property(value => value.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(value => value.ReceivedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(value => value.Revision).IsConcurrencyToken();
        builder.HasOne(value => value.ReturnAuthorization).WithMany(value => value.Receipts).HasForeignKey(value => value.ReturnAuthorizationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(value => value.ReturnLine).WithMany().HasForeignKey(value => value.ReturnLineId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.Item).WithMany().HasForeignKey(value => value.ItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.LicensePlate).WithMany().HasForeignKey(value => value.LicensePlateId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new { value.ReturnLineId, value.ItemId, value.LotId, value.SerialNumberId, value.InventoryStatusId });
    }
}
