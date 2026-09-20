using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class LicensePlateHistoryConfiguration : IEntityTypeConfiguration<LicensePlateHistory>
{
    public void Configure(EntityTypeBuilder<LicensePlateHistory> builder)
    {
        builder.ToTable("LicensePlateHistory");
        builder.HasKey(history => history.Id);
        builder.Property(history => history.Action)
            .HasConversion<int>()
            .IsRequired();
        builder.Property(history => history.UserId)
            .HasMaxLength(450)
            .IsRequired();
        builder.Property(history => history.OccurredAtUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(history => history.Quantity).HasColumnType("decimal(28,12)");
        builder.Property(history => history.ReferenceNumber).HasMaxLength(100);
        builder.Property(history => history.Reason).HasMaxLength(1_000);
        builder.Property(history => history.CreatedAt)
            .IsRequired()
            .HasColumnType("timestamp with time zone");
        builder.Property(history => history.UpdatedAt)
            .HasColumnType("timestamp with time zone");

        builder.HasOne(history => history.LicensePlate)
            .WithMany()
            .HasForeignKey(history => history.LicensePlateId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(history => history.Item)
            .WithMany()
            .HasForeignKey(history => history.ItemId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(history => history.Lot)
            .WithMany()
            .HasForeignKey(history => history.LotId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(history => history.SerialNumber)
            .WithMany()
            .HasForeignKey(history => history.SerialNumberId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(history => new { history.LicensePlateId, history.OccurredAtUtc });
        builder.HasIndex(history => history.ReferenceNumber);
        builder.HasIndex(history => history.UserId);
    }
}
