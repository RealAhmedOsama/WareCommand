using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class WarehouseWorkActivityConfiguration : IEntityTypeConfiguration<WarehouseWorkActivity>
{
    public void Configure(EntityTypeBuilder<WarehouseWorkActivity> builder)
    {
        builder.ToTable("WarehouseWorkActivities");
        builder.HasKey(activity => activity.Id);
        builder.Property(activity => activity.WorkerUserId).HasMaxLength(450).IsRequired();
        builder.Property(activity => activity.Category).HasConversion<int>().IsRequired();
        builder.Property(activity => activity.Reason).HasMaxLength(1_000);
        builder.Property(activity => activity.StartedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(activity => activity.EndedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(activity => activity.Revision).IsRequired().IsConcurrencyToken();
        builder.Property(activity => activity.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(activity => activity.UpdatedAt).HasColumnType("timestamp with time zone");
        builder.HasOne(activity => activity.Warehouse)
            .WithMany()
            .HasForeignKey(activity => activity.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(activity => activity.Work)
            .WithMany()
            .HasForeignKey(activity => activity.WarehouseWorkId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(activity => new { activity.WarehouseId, activity.WarehouseWorkId, activity.StartedAtUtc });
        builder.HasIndex(activity => new { activity.WarehouseId, activity.WorkerUserId, activity.Category, activity.StartedAtUtc });
    }
}
