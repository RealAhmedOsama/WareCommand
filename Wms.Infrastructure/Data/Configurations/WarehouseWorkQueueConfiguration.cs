using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class WarehouseWorkQueueConfiguration : IEntityTypeConfiguration<WarehouseWorkQueue>
{
    public void Configure(EntityTypeBuilder<WarehouseWorkQueue> builder)
    {
        builder.ToTable("WarehouseWorkQueues");
        builder.HasKey(queue => queue.Id);
        builder.Property(queue => queue.Code).HasMaxLength(50).IsRequired();
        builder.Property(queue => queue.Name).HasMaxLength(200).IsRequired();
        builder.Property(queue => queue.RequiredTeamCode).HasMaxLength(50);
        builder.Property(queue => queue.RequiredSkillCodesJson).HasMaxLength(4_000).IsRequired();
        builder.Property(queue => queue.RequiredCertificationCodesJson).HasMaxLength(4_000).IsRequired();
        builder.Property(queue => queue.AssignmentStrategy).HasConversion<int>().IsRequired();
        builder.Property(queue => queue.Revision).IsRequired().IsConcurrencyToken();
        builder.Property(queue => queue.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(queue => queue.UpdatedAt).HasColumnType("timestamp with time zone");
        builder.HasOne(queue => queue.Warehouse)
            .WithMany()
            .HasForeignKey(queue => queue.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(queue => queue.ZoneLocation)
            .WithMany()
            .HasForeignKey(queue => new { queue.WarehouseId, queue.ZoneLocationId })
            .HasPrincipalKey(location => new { location.WarehouseId, location.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(queue => new { queue.WarehouseId, queue.Code }).IsUnique();
        builder.HasIndex(queue => new { queue.WarehouseId, queue.IsActive, queue.WorkType, queue.Priority });
        builder.HasIndex(queue => new { queue.WarehouseId, queue.ZoneLocationId, queue.IsActive });
    }
}
