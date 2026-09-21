using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class CycleCountPlanConfiguration : IEntityTypeConfiguration<CycleCountPlan>
{
    public void Configure(EntityTypeBuilder<CycleCountPlan> builder)
    {
        builder.ToTable("CycleCountPlans", table => table.HasCheckConstraint(
            "CK_CycleCountPlans_ThresholdNonNegative",
            "\"ThresholdQuantity\" >= 0"));
        builder.HasKey(plan => plan.Id);
        builder.Property(plan => plan.PlanKey).HasMaxLength(100).IsRequired();
        builder.Property(plan => plan.ItemClass).HasMaxLength(20);
        builder.Property(plan => plan.FrequencyDays).IsRequired();
        builder.Property(plan => plan.ThresholdQuantity).HasColumnType("decimal(28,12)");
        builder.Property(plan => plan.FreezePolicy).HasConversion<int>().IsRequired();
        builder.Property(plan => plan.NextDueAtUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(plan => plan.Revision).IsRequired().IsConcurrencyToken();
        builder.Property(plan => plan.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(plan => plan.UpdatedAt).HasColumnType("timestamp with time zone");

        builder.HasOne(plan => plan.Warehouse)
            .WithMany()
            .HasForeignKey(plan => plan.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(plan => plan.Location)
            .WithMany()
            .HasForeignKey(plan => new { plan.WarehouseId, plan.LocationId })
            .HasPrincipalKey(location => new { location.WarehouseId, location.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(plan => plan.Item)
            .WithMany()
            .HasForeignKey(plan => plan.ItemId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(plan => new { plan.WarehouseId, plan.PlanKey }).IsUnique();
        builder.HasIndex(plan => new { plan.WarehouseId, plan.IsActive, plan.NextDueAtUtc });
        builder.HasIndex(plan => new { plan.WarehouseId, plan.LocationId, plan.ItemId });
    }
}
