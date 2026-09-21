using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class WarehouseWorkInterleavingPolicyConfiguration : IEntityTypeConfiguration<WarehouseWorkInterleavingPolicy>
{
    public void Configure(EntityTypeBuilder<WarehouseWorkInterleavingPolicy> builder)
    {
        builder.ToTable("WarehouseWorkInterleavingPolicies");
        builder.HasKey(policy => policy.Id);
        builder.Property(policy => policy.Code).HasMaxLength(50).IsRequired();
        builder.Property(policy => policy.Name).HasMaxLength(200).IsRequired();
        builder.Property(policy => policy.PriorityWeight).HasColumnType("decimal(18,6)").IsRequired();
        builder.Property(policy => policy.DeadlineWeight).HasColumnType("decimal(18,6)").IsRequired();
        builder.Property(policy => policy.TravelWeight).HasColumnType("decimal(18,6)").IsRequired();
        builder.Property(policy => policy.ZoneAffinityWeight).HasColumnType("decimal(18,6)").IsRequired();
        builder.Property(policy => policy.MaximumTravelMinutes).HasColumnType("decimal(18,6)");
        builder.Property(policy => policy.Revision).IsRequired().IsConcurrencyToken();
        builder.Property(policy => policy.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(policy => policy.UpdatedAt).HasColumnType("timestamp with time zone");

        builder.HasOne(policy => policy.Warehouse)
            .WithMany()
            .HasForeignKey(policy => policy.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(policy => new { policy.WarehouseId, policy.Code }).IsUnique();
        builder.HasIndex(policy => new { policy.WarehouseId, policy.IsActive });
    }
}
