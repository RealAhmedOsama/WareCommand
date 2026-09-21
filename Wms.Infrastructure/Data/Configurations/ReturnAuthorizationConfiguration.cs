using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class ReturnAuthorizationConfiguration : IEntityTypeConfiguration<ReturnAuthorization>
{
    public void Configure(EntityTypeBuilder<ReturnAuthorization> builder)
    {
        builder.ToTable("ReturnAuthorizations");
        builder.HasKey(value => value.Id);
        builder.Property(value => value.RmaNumber).HasMaxLength(80).IsRequired();
        builder.Property(value => value.Reason).HasMaxLength(1000).IsRequired();
        builder.Property(value => value.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(value => value.CreatedByUserId).HasMaxLength(450).IsRequired();
        builder.Property(value => value.AuthorizedByUserId).HasMaxLength(450);
        builder.Property(value => value.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(value => value.AuthorizedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(value => value.ReceivedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(value => value.ClosedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(value => value.Revision).IsConcurrencyToken();
        builder.HasOne(value => value.Warehouse).WithMany().HasForeignKey(value => value.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.Customer).WithMany().HasForeignKey(value => value.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.SalesOrder).WithMany().HasForeignKey(value => value.SalesOrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.Shipment).WithMany().HasForeignKey(value => value.ShipmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new { value.WarehouseId, value.RmaNumber }).IsUnique();
        builder.HasIndex(value => new { value.WarehouseId, value.Status });
    }
}
