using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class InventoryRecallCaseConfiguration
    : IEntityTypeConfiguration<InventoryRecallCase>
{
    public void Configure(EntityTypeBuilder<InventoryRecallCase> builder)
    {
        builder.ToTable("InventoryRecallCases");
        builder.HasKey(recallCase => recallCase.Id);
        builder.Property(recallCase => recallCase.CaseNumber).HasMaxLength(80).IsRequired();
        builder.Property(recallCase => recallCase.IdempotencyKey).HasMaxLength(250).IsRequired();
        builder.Property(recallCase => recallCase.Reason).HasMaxLength(1_000).IsRequired();
        builder.Property(recallCase => recallCase.CreatedByUserId).HasMaxLength(450).IsRequired();
        builder.Property(recallCase => recallCase.ExternalReference).HasMaxLength(200);
        builder.Property(recallCase => recallCase.Status).HasConversion<int>().IsRequired();
        builder.Property(recallCase => recallCase.ClosedByUserId).HasMaxLength(450);
        builder.Property(recallCase => recallCase.ClosureReason).HasMaxLength(1_000);
        builder.Property(recallCase => recallCase.CreatedAtUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(recallCase => recallCase.ClosedAtUtc)
            .HasColumnType("timestamp with time zone");
        builder.Property(recallCase => recallCase.Revision).IsRequired().IsConcurrencyToken();
        builder.Property(recallCase => recallCase.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(recallCase => recallCase.UpdatedAt)
            .HasColumnType("timestamp with time zone");

        builder.HasOne(recallCase => recallCase.Warehouse)
            .WithMany()
            .HasForeignKey(recallCase => recallCase.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(recallCase => recallCase.Item)
            .WithMany()
            .HasForeignKey(recallCase => recallCase.ItemId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(recallCase => recallCase.Lot)
            .WithMany()
            .HasForeignKey(recallCase => recallCase.LotId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(recallCase => recallCase.Serial)
            .WithMany()
            .HasForeignKey(recallCase => recallCase.SerialNumberId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(recallCase => recallCase.LicensePlate)
            .WithMany()
            .HasForeignKey(recallCase => recallCase.LicensePlateId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(recallCase => recallCase.CaseNumber).IsUnique();
        builder.HasIndex(recallCase => new
        {
            recallCase.WarehouseId,
            recallCase.IdempotencyKey
        }).IsUnique();
        builder.HasIndex(recallCase => new
        {
            recallCase.WarehouseId,
            recallCase.Status,
            recallCase.CreatedAtUtc
        });
        builder.HasIndex(recallCase => recallCase.LotId);
        builder.HasIndex(recallCase => recallCase.SerialNumberId);
        builder.HasIndex(recallCase => recallCase.LicensePlateId);
    }
}
