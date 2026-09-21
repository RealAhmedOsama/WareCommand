using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class CycleCountTaskConfiguration : IEntityTypeConfiguration<CycleCountTask>
{
    public void Configure(EntityTypeBuilder<CycleCountTask> builder)
    {
        builder.ToTable("CycleCountTasks");
        builder.HasKey(task => task.Id);
        builder.Property(task => task.TaskKey).HasMaxLength(250).IsRequired();
        builder.Property(task => task.TaskNumber).HasMaxLength(80).IsRequired();
        builder.Property(task => task.FreezePolicy).HasConversion<int>().IsRequired();
        builder.Property(task => task.Status).HasConversion<int>().IsRequired();
        builder.Property(task => task.CreatedByUserId).HasMaxLength(450).IsRequired();
        builder.Property(task => task.StartedByUserId).HasMaxLength(450);
        builder.Property(task => task.ApprovedByUserId).HasMaxLength(450);
        builder.Property(task => task.ApprovalReason).HasMaxLength(1_000);
        builder.Property(task => task.SnapshotAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(task => task.StartedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(task => task.SubmittedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(task => task.ApprovedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(task => task.Revision).IsRequired().IsConcurrencyToken();
        builder.Property(task => task.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(task => task.UpdatedAt).HasColumnType("timestamp with time zone");

        builder.HasOne(task => task.Plan)
            .WithMany(plan => plan.Tasks)
            .HasForeignKey(task => task.PlanId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(task => task.Warehouse)
            .WithMany()
            .HasForeignKey(task => task.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(task => task.Location)
            .WithMany()
            .HasForeignKey(task => new { task.WarehouseId, task.LocationId })
            .HasPrincipalKey(location => new { location.WarehouseId, location.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(task => task.WarehouseWork)
            .WithMany()
            .HasForeignKey(task => task.WarehouseWorkId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(task => task.TaskNumber).IsUnique();
        builder.HasIndex(task => task.TaskKey).IsUnique();
        builder.HasIndex(task => new { task.WarehouseId, task.Status, task.SnapshotAtUtc });
    }
}
