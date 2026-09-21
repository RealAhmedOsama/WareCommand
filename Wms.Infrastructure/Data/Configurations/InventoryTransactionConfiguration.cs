using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class InventoryTransactionConfiguration : IEntityTypeConfiguration<InventoryTransaction>
{
    public void Configure(EntityTypeBuilder<InventoryTransaction> builder)
    {
        builder.ToTable("InventoryTransactions");
        builder.HasKey(transaction => transaction.Id);

        builder.Property(transaction => transaction.Type)
            .HasConversion<int>()
            .IsRequired();
        builder.Property(transaction => transaction.BaseUnitOfMeasure)
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(transaction => transaction.OwnerKind)
            .HasConversion<int>()
            .IsRequired();
        builder.Property(transaction => transaction.OwnerCodeSnapshot)
            .HasMaxLength(80)
            .IsRequired();
        builder.Property(transaction => transaction.SerialNumber)
            .HasMaxLength(100);
        builder.Property(transaction => transaction.QuantityDelta)
            .HasColumnType("decimal(28,12)")
            .IsRequired();
        builder.Property(transaction => transaction.QuantityBefore)
            .HasColumnType("decimal(28,12)")
            .IsRequired();
        builder.Property(transaction => transaction.QuantityAfter)
            .HasColumnType("decimal(28,12)")
            .IsRequired();
        builder.Property(transaction => transaction.ReservedQuantityDelta)
            .HasColumnType("decimal(28,12)")
            .IsRequired();
        builder.Property(transaction => transaction.ReservedQuantityBefore)
            .HasColumnType("decimal(28,12)")
            .IsRequired();
        builder.Property(transaction => transaction.ReservedQuantityAfter)
            .HasColumnType("decimal(28,12)")
            .IsRequired();
        builder.Property(transaction => transaction.ReferenceType)
            .HasMaxLength(100);
        builder.Property(transaction => transaction.ReferenceId)
            .HasMaxLength(200);
        builder.Property(transaction => transaction.Reason)
            .HasMaxLength(1_000);
        builder.Property(transaction => transaction.ActorUserId)
            .HasMaxLength(450)
            .IsRequired();
        builder.Property(transaction => transaction.OccurredAtUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(transaction => transaction.CorrelationId)
            .HasMaxLength(100)
            .IsRequired();
        builder.Property(transaction => transaction.IdempotencyKey)
            .HasMaxLength(250)
            .IsRequired();
        builder.Property(transaction => transaction.TransactionGroupId)
            .HasMaxLength(100)
            .IsRequired();
        builder.Property(transaction => transaction.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(transaction => transaction.UpdatedAt)
            .HasColumnType("timestamp with time zone");

        builder.HasOne(transaction => transaction.Warehouse)
            .WithMany()
            .HasForeignKey(transaction => transaction.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(transaction => transaction.Location)
            .WithMany()
            .HasForeignKey(transaction => new { transaction.WarehouseId, transaction.LocationId })
            .HasPrincipalKey(location => new { location.WarehouseId, location.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(transaction => transaction.Item)
            .WithMany()
            .HasForeignKey(transaction => transaction.ItemId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(transaction => transaction.Lot)
            .WithMany()
            .HasForeignKey(transaction => transaction.LotId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(transaction => transaction.Serial)
            .WithMany()
            .HasForeignKey(transaction => transaction.SerialNumberId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(transaction => transaction.LicensePlate)
            .WithMany()
            .HasForeignKey(transaction => transaction.LicensePlateId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(transaction => transaction.InventoryStatus)
            .WithMany()
            .HasForeignKey(transaction => transaction.InventoryStatusId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(transaction => transaction.InventoryOwner)
            .WithMany()
            .HasForeignKey(transaction => transaction.InventoryOwnerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(transaction => transaction.Movement)
            .WithMany()
            .HasForeignKey(transaction => transaction.MovementId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(transaction => transaction.ReversalOfTransaction)
            .WithMany()
            .HasForeignKey(transaction => transaction.ReversalOfTransactionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(transaction => new
        {
            transaction.IdempotencyKey,
            transaction.EntrySequence
        }).IsUnique();
        builder.HasIndex(transaction => new
        {
            transaction.WarehouseId,
            transaction.LocationId,
            transaction.ItemId,
            transaction.OccurredAtUtc
        });
        builder.HasIndex(transaction => new
        {
            transaction.WarehouseId,
            transaction.ItemId,
            transaction.OccurredAtUtc
        });
        builder.HasIndex(transaction => new
        {
            transaction.ReferenceType,
            transaction.ReferenceId
        });
        builder.HasIndex(transaction => transaction.MovementId);
        builder.HasIndex(transaction => transaction.InventoryOwnerId);
        builder.HasIndex(transaction => transaction.ReversalOfTransactionId);
        builder.HasIndex(transaction => new
        {
            transaction.WarehouseId,
            transaction.TransactionGroupId,
            transaction.EntrySequence
        });
    }
}
