using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class PackingSessionConfiguration : IEntityTypeConfiguration<PackingSession>
{
    public void Configure(EntityTypeBuilder<PackingSession> builder)
    {
        builder.ToTable("PackingSessions");
        builder.HasKey(session => session.Id);
        builder.Property(session => session.SessionNumber).IsRequired().HasMaxLength(80);
        builder.Property(session => session.SourceReference).IsRequired().HasMaxLength(200);
        builder.Property(session => session.SourceType).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(session => session.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(session => session.UserId).IsRequired().HasMaxLength(450);
        builder.Property(session => session.StartedAtUtc).IsRequired().HasColumnType("timestamp with time zone");
        builder.Property(session => session.CompletedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(session => session.CancelledAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(session => session.CancellationReason).HasMaxLength(1_000);
        builder.Property(session => session.Revision).IsRequired().IsConcurrencyToken();
        builder.Property(session => session.CreatedAt).IsRequired().HasColumnType("timestamp with time zone");
        builder.Property(session => session.UpdatedAt).HasColumnType("timestamp with time zone");

        builder.HasOne(session => session.Warehouse)
            .WithMany()
            .HasForeignKey(session => session.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(session => session.PackingStation)
            .WithMany()
            .HasForeignKey(session => session.PackingStationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(session => session.SalesOrder)
            .WithMany()
            .HasForeignKey(session => session.SalesOrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(session => session.StagingLicensePlate)
            .WithMany()
            .HasForeignKey(session => session.StagingLicensePlateId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(session => session.Packages)
            .WithOne(package => package.PackingSession)
            .HasForeignKey(package => package.PackingSessionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(session => session.SessionNumber).IsUnique();
        builder.HasIndex(session => new { session.WarehouseId, session.Status, session.StartedAtUtc });
        builder.HasIndex(session => new { session.PackingStationId, session.Status });
    }
}
