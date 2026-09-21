using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class SlottingPolicyConfiguration : IEntityTypeConfiguration<SlottingPolicy>
{
    public void Configure(EntityTypeBuilder<SlottingPolicy> builder)
    {
        builder.ToTable("SlottingPolicies");
        builder.HasKey(policy => policy.Id);
        builder.Property(policy => policy.PolicyKey).HasMaxLength(80).IsRequired();
        builder.Property(policy => policy.Name).HasMaxLength(200).IsRequired();
        builder.Property(policy => policy.LookbackDays).IsRequired();
        builder.Property(policy => policy.VelocityWeight).HasColumnType("decimal(9,4)").IsRequired();
        builder.Property(policy => policy.TravelWeight).HasColumnType("decimal(9,4)").IsRequired();
        builder.Property(policy => policy.SpaceWeight).HasColumnType("decimal(9,4)").IsRequired();
        builder.Property(policy => policy.ReplenishmentWeight).HasColumnType("decimal(9,4)").IsRequired();
        builder.Property(policy => policy.AffinityWeight).HasColumnType("decimal(9,4)").IsRequired();
        builder.Property(policy => policy.MaxRecommendationsPerItem).IsRequired();
        builder.Property(policy => policy.RecommendationExpiryDays).IsRequired();
        builder.Property(policy => policy.AllowedLocationTypes).HasMaxLength(300).IsRequired();
        builder.Property(policy => policy.EffectiveFromUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(policy => policy.EffectiveToUtc).HasColumnType("timestamp with time zone");
        builder.Property(policy => policy.IsActive).IsRequired();
        builder.Property(policy => policy.Revision).IsRequired().IsConcurrencyToken();
        builder.Property(policy => policy.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(policy => policy.UpdatedAt).HasColumnType("timestamp with time zone");

        builder.HasOne(policy => policy.Warehouse)
            .WithMany()
            .HasForeignKey(policy => policy.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(policy => new { policy.WarehouseId, policy.PolicyKey }).IsUnique();
        builder.HasIndex(policy => new
        {
            policy.WarehouseId,
            policy.IsActive,
            policy.EffectiveFromUtc,
            policy.EffectiveToUtc
        });
    }
}
