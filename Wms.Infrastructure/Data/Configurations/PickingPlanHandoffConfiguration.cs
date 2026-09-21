using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class PickingPlanHandoffConfiguration : IEntityTypeConfiguration<PickingPlanHandoff>
{
    public void Configure(EntityTypeBuilder<PickingPlanHandoff> builder)
    {
        builder.ToTable("PickingPlanHandoffs");
        builder.HasKey(handoff => handoff.Id);
        builder.Property(handoff => handoff.ExpectedContainerScanCode).HasMaxLength(120).IsRequired();
        builder.Property(handoff => handoff.Status).HasConversion<int>().IsRequired();
        builder.Property(handoff => handoff.CompletedByUserId).HasMaxLength(450);
        builder.Property(handoff => handoff.CompletedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(handoff => handoff.Revision).IsRequired().IsConcurrencyToken();
        builder.Property(handoff => handoff.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(handoff => handoff.UpdatedAt).HasColumnType("timestamp with time zone");

        builder.HasOne(handoff => handoff.Plan)
            .WithMany(plan => plan.Handoffs)
            .HasForeignKey(handoff => handoff.PickingPlanId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(handoff => handoff.Container)
            .WithMany(container => container.Handoffs)
            .HasForeignKey(handoff => handoff.PickingPlanContainerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(handoff => handoff.FromZoneLocation)
            .WithMany()
            .HasForeignKey(handoff => handoff.FromZoneLocationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(handoff => handoff.ToZoneLocation)
            .WithMany()
            .HasForeignKey(handoff => handoff.ToZoneLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(handoff => new { handoff.PickingPlanId, handoff.Sequence }).IsUnique();
        builder.HasIndex(handoff => new { handoff.PickingPlanId, handoff.Status });
    }
}
