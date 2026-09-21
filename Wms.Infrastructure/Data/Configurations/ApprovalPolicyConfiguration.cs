using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class ApprovalPolicyConfiguration : IEntityTypeConfiguration<ApprovalPolicy>
{
    public void Configure(EntityTypeBuilder<ApprovalPolicy> builder)
    {
        builder.ToTable("ApprovalPolicies");
        builder.HasKey(policy => policy.Id);
        builder.Property(policy => policy.Code).HasMaxLength(80).IsRequired();
        builder.Property(policy => policy.Name).HasMaxLength(200).IsRequired();
        builder.Property(policy => policy.Module).HasMaxLength(100).IsRequired();
        builder.Property(policy => policy.Operation).HasMaxLength(150).IsRequired();
        builder.Property(policy => policy.ReasonCode).HasMaxLength(80);
        builder.Property(policy => policy.ItemRisk).HasMaxLength(50);
        builder.Property(policy => policy.StatusRisk).HasMaxLength(50);
        builder.Property(policy => policy.MinimumQuantity).HasColumnType("decimal(18,4)");
        builder.Property(policy => policy.MinimumValue).HasColumnType("decimal(18,4)");
        builder.Property(policy => policy.MinimumVariancePercent).HasColumnType("decimal(18,4)");
        builder.Property(policy => policy.ApprovalLevelsJson).HasMaxLength(4_000).IsRequired();
        builder.Property(policy => policy.EffectiveFromUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(policy => policy.EffectiveToUtc)
            .HasColumnType("timestamp with time zone");
        builder.Property(policy => policy.Revision).IsRequired().IsConcurrencyToken();
        builder.Property(policy => policy.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(policy => policy.UpdatedAt)
            .HasColumnType("timestamp with time zone");

        builder.HasOne<Warehouse>()
            .WithMany()
            .HasForeignKey(policy => policy.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(policy => new { policy.WarehouseId, policy.Code }).IsUnique();
        builder.HasIndex(policy => new { policy.Module, policy.Operation, policy.IsActive });
        builder.HasIndex(policy => new { policy.WarehouseId, policy.IsActive, policy.EffectiveFromUtc });
    }
}
