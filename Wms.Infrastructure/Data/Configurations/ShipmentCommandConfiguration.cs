using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class ShipmentCommandConfiguration : IEntityTypeConfiguration<ShipmentCommand>
{
    public void Configure(EntityTypeBuilder<ShipmentCommand> builder)
    {
        builder.ToTable("ShipmentCommands");
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Operation).HasMaxLength(100).IsRequired();
        builder.Property(value => value.IdempotencyKey).HasMaxLength(250).IsRequired();
        builder.Property(value => value.RequestHash).HasMaxLength(64).IsRequired();
        builder.Property(value => value.UserId).HasMaxLength(450).IsRequired();
        builder.Property(value => value.ExecutedAtUtc).HasColumnType("timestamp with time zone");
        builder.HasOne(value => value.Shipment).WithMany().HasForeignKey(value => value.ShipmentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(value => new { value.ShipmentId, value.Operation, value.IdempotencyKey }).IsUnique();
    }
}
