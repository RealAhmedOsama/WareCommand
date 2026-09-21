using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class WarehouseWorkerProfileConfiguration : IEntityTypeConfiguration<WarehouseWorkerProfile>
{
    public void Configure(EntityTypeBuilder<WarehouseWorkerProfile> builder)
    {
        builder.ToTable("WarehouseWorkerProfiles");
        builder.HasKey(profile => profile.Id);
        builder.Property(profile => profile.UserId).HasMaxLength(450).IsRequired();
        builder.Property(profile => profile.TeamCode).HasMaxLength(50);
        builder.Property(profile => profile.ShiftCode).HasMaxLength(50);
        builder.Property(profile => profile.TimeZoneId).HasMaxLength(100).IsRequired();
        builder.Property(profile => profile.SkillCodesJson).HasMaxLength(4_000).IsRequired();
        builder.Property(profile => profile.CertificationCodesJson).HasMaxLength(4_000).IsRequired();
        builder.Property(profile => profile.PreferredZoneLocationIdsJson).HasMaxLength(4_000).IsRequired();
        builder.Property(profile => profile.ShiftStartAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(profile => profile.ShiftEndAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(profile => profile.Revision).IsRequired().IsConcurrencyToken();
        builder.Property(profile => profile.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(profile => profile.UpdatedAt).HasColumnType("timestamp with time zone");
        builder.HasOne(profile => profile.Warehouse)
            .WithMany()
            .HasForeignKey(profile => profile.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(profile => new { profile.UserId, profile.WarehouseId }).IsUnique();
        builder.HasIndex(profile => new { profile.WarehouseId, profile.IsActive, profile.TeamCode });
        builder.HasIndex(profile => new { profile.WarehouseId, profile.ShiftStartAtUtc, profile.ShiftEndAtUtc });
    }
}
