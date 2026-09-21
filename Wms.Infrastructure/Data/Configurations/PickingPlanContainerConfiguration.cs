using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class PickingPlanContainerConfiguration : IEntityTypeConfiguration<PickingPlanContainer>
{
    public void Configure(EntityTypeBuilder<PickingPlanContainer> builder)
    {
        builder.ToTable("PickingPlanContainers");
        builder.HasKey(container => container.Id);
        builder.Property(container => container.ContainerKey).HasMaxLength(120).IsRequired();
        builder.Property(container => container.ExpectedScanCode).HasMaxLength(120).IsRequired();
        builder.Property(container => container.Status).HasConversion<int>().IsRequired();
        builder.Property(container => container.ScannedByUserId).HasMaxLength(450);
        builder.Property(container => container.ScannedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(container => container.Revision).IsRequired().IsConcurrencyToken();
        builder.Property(container => container.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(container => container.UpdatedAt).HasColumnType("timestamp with time zone");

        builder.HasOne(container => container.Plan)
            .WithMany(plan => plan.Containers)
            .HasForeignKey(container => container.PickingPlanId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(container => container.SalesOrder)
            .WithMany()
            .HasForeignKey(container => container.SalesOrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(container => container.ZoneLocation)
            .WithMany()
            .HasForeignKey(container => container.ZoneLocationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(container => container.TargetLicensePlate)
            .WithMany()
            .HasForeignKey(container => container.TargetLicensePlateId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(container => new { container.PickingPlanId, container.ContainerKey }).IsUnique();
        builder.HasIndex(container => new { container.PickingPlanId, container.Sequence }).IsUnique();
    }
}
