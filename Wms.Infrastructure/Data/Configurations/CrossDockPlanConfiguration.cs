using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class CrossDockPlanConfiguration : IEntityTypeConfiguration<CrossDockPlan>
{
    public void Configure(EntityTypeBuilder<CrossDockPlan> builder)
    {
        builder.ToTable("CrossDockPlans");
        builder.HasKey(plan => plan.Id);
        builder.Property(plan => plan.PlanNumber).HasMaxLength(80).IsRequired();
        builder.Property(plan => plan.CreationKey).HasMaxLength(250).IsRequired();
        builder.Property(plan => plan.Mode).HasConversion<int>().IsRequired();
        builder.Property(plan => plan.Status).HasConversion<int>().IsRequired();
        builder.Property(plan => plan.BaseUnitOfMeasure).HasMaxLength(20).IsRequired();
        builder.Property(plan => plan.LotNumber).HasMaxLength(100);
        builder.Property(plan => plan.SerialNumber).HasMaxLength(100);
        builder.Property(plan => plan.Explanation).HasMaxLength(2_000).IsRequired();
        builder.Property(plan => plan.CreatedByUserId).HasMaxLength(450).IsRequired();
        builder.Property(plan => plan.CancelledByUserId).HasMaxLength(450);
        builder.Property(plan => plan.CancellationReason).HasMaxLength(1_000);
        builder.Property(plan => plan.ExpiryDate).HasColumnType("timestamp with time zone");
        builder.Property(plan => plan.ReceivedBaseQuantity).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(plan => plan.MatchedBaseQuantity).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(plan => plan.FallbackBaseQuantity).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(plan => plan.Revision).IsRequired().IsConcurrencyToken();
        builder.Property(plan => plan.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(plan => plan.UpdatedAt).HasColumnType("timestamp with time zone");

        builder.HasOne(plan => plan.Warehouse)
            .WithMany()
            .HasForeignKey(plan => plan.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(plan => plan.Receipt)
            .WithMany()
            .HasForeignKey(plan => plan.ReceiptId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(plan => plan.ReceiptLine)
            .WithMany()
            .HasForeignKey(plan => plan.ReceiptLineId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(plan => plan.Item)
            .WithMany()
            .HasForeignKey(plan => plan.ItemId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(plan => plan.Policy)
            .WithMany()
            .HasForeignKey(plan => plan.PolicyId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(plan => plan.SourceLocation)
            .WithMany()
            .HasForeignKey(plan => new { plan.WarehouseId, plan.SourceLocationId })
            .HasPrincipalKey(location => new { location.WarehouseId, location.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(plan => plan.DestinationLocation)
            .WithMany()
            .HasForeignKey(plan => new { plan.WarehouseId, plan.DestinationLocationId })
            .HasPrincipalKey(location => new { location.WarehouseId, location.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(plan => new { plan.WarehouseId, plan.CreationKey }).IsUnique();
        builder.HasIndex(plan => new { plan.WarehouseId, plan.ReceiptLineId, plan.Status });
        builder.HasIndex(plan => plan.PolicyId);

        builder.HasMany(plan => plan.Lines)
            .WithOne(line => line.Plan)
            .HasForeignKey(line => line.CrossDockPlanId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
