using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;
using WarehouseWorkEntity = Wms.Domain.Entities.WarehouseWork;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class WarehouseWorkConfiguration : IEntityTypeConfiguration<WarehouseWorkEntity>
{
    public void Configure(EntityTypeBuilder<WarehouseWorkEntity> builder)
    {
        builder.ToTable("WarehouseWorks");
        builder.HasKey(work => work.Id);
        builder.Property(work => work.WorkNumber).HasMaxLength(80).IsRequired();
        builder.Property(work => work.CreationKey).HasMaxLength(250).IsRequired();
        builder.Property(work => work.SourceEntityType).HasMaxLength(100).IsRequired();
        builder.Property(work => work.SourceEntityId).HasMaxLength(200).IsRequired();
        builder.Property(work => work.SourceLineReference).HasMaxLength(100);
        builder.Property(work => work.QueueCode).HasMaxLength(50).IsRequired();
        builder.Property(work => work.TeamCode).HasMaxLength(50);
        builder.Property(work => work.Notes).HasMaxLength(2_000);
        builder.Property(work => work.AssignedUserId).HasMaxLength(450);
        builder.Property(work => work.AssignedTeamCode).HasMaxLength(50);
        builder.Property(work => work.AssignedByUserId).HasMaxLength(450);
        builder.Property(work => work.CompletedByUserId).HasMaxLength(450);
        builder.Property(work => work.CancelledByUserId).HasMaxLength(450);
        builder.Property(work => work.CancellationReason).HasMaxLength(1_000);
        builder.Property(work => work.ExceptionReason).HasMaxLength(1_000);
        builder.Property(work => work.ExceptionByUserId).HasMaxLength(450);
        builder.Property(work => work.OverrideReason).HasMaxLength(1_000);
        builder.Property(work => work.Revision).IsRequired().IsConcurrencyToken();
        builder.Property(work => work.DueAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(work => work.AssignedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(work => work.StartedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(work => work.PausedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(work => work.CompletedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(work => work.CancelledAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(work => work.ExceptionAtUtc).HasColumnType("timestamp with time zone");

        builder.HasOne(work => work.Warehouse)
            .WithMany()
            .HasForeignKey(work => work.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(work => work.WorkNumber).IsUnique();
        builder.HasIndex(work => new { work.WarehouseId, work.CreationKey }).IsUnique();
        builder.HasIndex(work => new { work.WarehouseId, work.Status, work.QueueCode, work.Priority });
        builder.HasIndex(work => new { work.AssignedUserId, work.Status });
        builder.HasIndex(work => new { work.SourceEntityType, work.SourceEntityId, work.SourceLineReference });
    }
}
