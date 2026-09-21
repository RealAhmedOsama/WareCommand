using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class PickingPlanConfiguration : IEntityTypeConfiguration<PickingPlan>
{
    public void Configure(EntityTypeBuilder<PickingPlan> builder)
    {
        builder.ToTable("PickingPlans");
        builder.HasKey(plan => plan.Id);
        builder.Property(plan => plan.PlanNumber).HasMaxLength(80).IsRequired();
        builder.Property(plan => plan.CreationKey).HasMaxLength(250).IsRequired();
        builder.Property(plan => plan.Strategy).HasConversion<int>().IsRequired();
        builder.Property(plan => plan.MaxWeightKg).HasColumnType("decimal(28,12)");
        builder.Property(plan => plan.MaxVolumeCubicMeters).HasColumnType("decimal(28,12)");
        builder.Property(plan => plan.CreatedByUserId).HasMaxLength(450).IsRequired();
        builder.Property(plan => plan.Status).HasConversion<int>().IsRequired();
        builder.Property(plan => plan.LastError).HasMaxLength(2_000);
        builder.Property(plan => plan.Revision).IsRequired().IsConcurrencyToken();
        builder.Property(plan => plan.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(plan => plan.UpdatedAt).HasColumnType("timestamp with time zone");

        builder.HasOne(plan => plan.Warehouse)
            .WithMany()
            .HasForeignKey(plan => plan.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(plan => plan.Wave)
            .WithMany()
            .HasForeignKey(plan => plan.WaveId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(plan => plan.Policy)
            .WithMany()
            .HasForeignKey(plan => plan.PolicyId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(plan => plan.Lines)
            .WithOne(line => line.Plan)
            .HasForeignKey(line => line.PickingPlanId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(plan => plan.Containers)
            .WithOne(container => container.Plan)
            .HasForeignKey(container => container.PickingPlanId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(plan => plan.Handoffs)
            .WithOne(handoff => handoff.Plan)
            .HasForeignKey(handoff => handoff.PickingPlanId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(plan => plan.PlanNumber).IsUnique();
        builder.HasIndex(plan => new { plan.WarehouseId, plan.CreationKey }).IsUnique();
        builder.HasIndex(plan => new { plan.WarehouseId, plan.Status, plan.Strategy });
    }
}
