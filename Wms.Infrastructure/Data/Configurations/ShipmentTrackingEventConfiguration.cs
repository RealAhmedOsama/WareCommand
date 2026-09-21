using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class ShipmentTrackingEventConfiguration : IEntityTypeConfiguration<ShipmentTrackingEvent>
{
    public void Configure(EntityTypeBuilder<ShipmentTrackingEvent> builder)
    {
        builder.ToTable("ShipmentTrackingEvents");
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Status).HasMaxLength(100).IsRequired();
        builder.Property(value => value.TrackingNumber).HasMaxLength(250);
        builder.Property(value => value.ProviderReference).HasMaxLength(250);
        builder.Property(value => value.Source).HasMaxLength(50).IsRequired();
        builder.Property(value => value.PayloadReference).HasMaxLength(500);
        builder.Property(value => value.OccurredAtUtc).HasColumnType("timestamp with time zone");
        builder.HasOne(value => value.Shipment).WithMany(value => value.TrackingEvents).HasForeignKey(value => value.ShipmentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(value => new { value.ShipmentId, value.OccurredAtUtc });
    }
}
