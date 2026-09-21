using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class ValueAddedServiceOrderConfiguration : IEntityTypeConfiguration<ValueAddedServiceOrder>
{
    public void Configure(EntityTypeBuilder<ValueAddedServiceOrder> builder)
    {
        builder.ToTable("ValueAddedServiceOrders");
        builder.HasKey(order => order.Id);
        builder.Property(order => order.OrderNumber).HasMaxLength(80).IsRequired();
        builder.Property(order => order.IdempotencyKey).HasMaxLength(250).IsRequired();
        builder.Property(order => order.Type).HasConversion<int>().IsRequired();
        builder.Property(order => order.Status).HasConversion<int>().IsRequired();
        builder.Property(order => order.OutputUnitOfMeasure).HasMaxLength(20).IsRequired();
        builder.Property(order => order.OutputOwnerKind).HasConversion<int>().IsRequired();
        builder.Property(order => order.OutputOwnerCodeSnapshot).HasMaxLength(80).IsRequired();
        builder.Property(order => order.InputOwnerKind).HasConversion<int>().IsRequired();
        builder.Property(order => order.InputOwnerCodeSnapshot).HasMaxLength(80).IsRequired();
        builder.Property(order => order.InputSerialNumber).HasMaxLength(100);
        builder.Property(order => order.InstructionSnapshot).HasMaxLength(8_000);
        builder.Property(order => order.LocalizedInstructionSnapshot).HasMaxLength(8_000);
        builder.Property(order => order.StationCode).HasMaxLength(50);
        builder.Property(order => order.LabelTemplateCode).HasMaxLength(120);
        builder.Property(order => order.CreatedByUserId).HasMaxLength(450).IsRequired();
        builder.Property(order => order.CancelledByUserId).HasMaxLength(450);
        builder.Property(order => order.CancellationReason).HasMaxLength(1_000);
        builder.Property(order => order.ReversedByUserId).HasMaxLength(450);
        builder.Property(order => order.ReversalReason).HasMaxLength(1_000);
        builder.Property(order => order.ExceptionReason).HasMaxLength(1_000);
        builder.Property(order => order.RequestedOutputQuantity).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(order => order.CompletedOutputQuantity).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(order => order.ScrapQuantity).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(order => order.ReversedOutputQuantity).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(order => order.CreatedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(order => order.ReleasedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(order => order.CompletedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(order => order.CancelledAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(order => order.ReversedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(order => order.Revision).IsRequired().IsConcurrencyToken();

        builder.HasOne(order => order.Warehouse)
            .WithMany()
            .HasForeignKey(order => order.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(order => order.SourceLocation)
            .WithMany()
            .HasForeignKey(order => order.SourceLocationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(order => order.DestinationLocation)
            .WithMany()
            .HasForeignKey(order => order.DestinationLocationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(order => order.OutputItem)
            .WithMany()
            .HasForeignKey(order => order.OutputItemId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(order => order.KitDefinition)
            .WithMany()
            .HasForeignKey(order => order.KitDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(order => order.WarehouseWork)
            .WithMany()
            .HasForeignKey(order => order.WarehouseWorkId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<InventoryOwner>()
            .WithMany()
            .HasForeignKey(order => order.OutputInventoryOwnerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<QualityProfile>()
            .WithMany()
            .HasForeignKey(order => order.QualityProfileId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(order => order.Lines)
            .WithOne(line => line.Order)
            .HasForeignKey(line => line.ValueAddedServiceOrderId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(order => order.TraceLinks)
            .WithOne(link => link.Order)
            .HasForeignKey(link => link.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(order => order.OrderNumber).IsUnique();
        builder.HasIndex(order => order.IdempotencyKey).IsUnique();
        builder.HasIndex(order => new { order.WarehouseId, order.Status, order.Type });
        builder.HasIndex(order => order.WarehouseWorkId).IsUnique();
    }
}
