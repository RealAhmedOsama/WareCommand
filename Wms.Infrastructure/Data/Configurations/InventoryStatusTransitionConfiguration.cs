using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class InventoryStatusTransitionConfiguration : IEntityTypeConfiguration<InventoryStatusTransition>
{
    public void Configure(EntityTypeBuilder<InventoryStatusTransition> builder)
    {
        builder.ToTable("InventoryStatusTransitions");
        builder.HasKey(transition => transition.Id);
        builder.Property(transition => transition.RequiresReason).IsRequired();
        builder.Property(transition => transition.IsActive).IsRequired();
        builder.Property(transition => transition.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(transition => transition.UpdatedAt)
            .HasColumnType("timestamp with time zone");

        builder.HasOne(transition => transition.FromInventoryStatus)
            .WithMany()
            .HasForeignKey(transition => transition.FromInventoryStatusId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(transition => transition.ToInventoryStatus)
            .WithMany()
            .HasForeignKey(transition => transition.ToInventoryStatusId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(transition => new
        {
            transition.FromInventoryStatusId,
            transition.ToInventoryStatusId
        }).IsUnique();
        builder.HasIndex(transition => transition.IsActive);

        builder.HasData(InventoryStatusCatalog.SystemTransitions.Select(transition => new
        {
            transition.Id,
            transition.FromStatusId,
            transition.ToStatusId,
            transition.RequiresReason,
            IsActive = true,
            CreatedAt = DateTime.UnixEpoch,
            UpdatedAt = (DateTime?)null
        }).Select(transition => new
        {
            transition.Id,
            FromInventoryStatusId = transition.FromStatusId,
            ToInventoryStatusId = transition.ToStatusId,
            transition.RequiresReason,
            transition.IsActive,
            transition.CreatedAt,
            transition.UpdatedAt
        }).ToArray());
    }
}
