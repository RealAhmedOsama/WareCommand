using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class WarehouseNumberSequenceConfiguration : IEntityTypeConfiguration<WarehouseNumberSequence>
{
    public void Configure(EntityTypeBuilder<WarehouseNumberSequence> builder)
    {
        builder.ToTable("WarehouseNumberSequences");

        builder.HasKey(sequence => sequence.Id);

        builder.Property(sequence => sequence.NextReceiptNumber).IsRequired();
        builder.Property(sequence => sequence.NextOrderNumber).IsRequired();
        builder.Property(sequence => sequence.NextWorkNumber).IsRequired();
        builder.Property(sequence => sequence.NextShipmentNumber).IsRequired();
        builder.Property(sequence => sequence.NextTransferNumber).IsRequired();
        builder.Property(sequence => sequence.NextCountNumber).IsRequired();
        builder.Property(sequence => sequence.Revision).IsRequired().IsConcurrencyToken();

        builder.Property(sequence => sequence.CreatedAt)
            .IsRequired()
            .HasColumnType("timestamp with time zone");

        builder.Property(sequence => sequence.UpdatedAt)
            .HasColumnType("timestamp with time zone");

        builder.HasOne(sequence => sequence.Warehouse)
            .WithOne(warehouse => warehouse.NumberSequence)
            .HasForeignKey<WarehouseNumberSequence>(sequence => sequence.WarehouseId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(sequence => sequence.WarehouseId).IsUnique();
    }
}
