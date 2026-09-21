using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class ShipmentLoadConfiguration : IEntityTypeConfiguration<ShipmentLoad>
{
    public void Configure(EntityTypeBuilder<ShipmentLoad> builder)
    {
        builder.ToTable("ShipmentLoads");
        builder.HasKey(value => value.Id);
        builder.Property(value => value.TrailerNumber).HasMaxLength(100);
        builder.Property(value => value.RouteReference).HasMaxLength(150);
        builder.Property(value => value.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(value => value.OpenedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(value => value.ClosedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(value => value.Revision).IsConcurrencyToken();
        builder.HasOne(value => value.Shipment).WithMany(value => value.Loads).HasForeignKey(value => value.ShipmentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(value => value.DockLocation).WithMany().HasForeignKey(value => value.DockLocationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new { value.ShipmentId, value.Status });
    }
}
