using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class LicensePlateNumberSequenceConfiguration : IEntityTypeConfiguration<LicensePlateNumberSequence>
{
    public void Configure(EntityTypeBuilder<LicensePlateNumberSequence> builder)
    {
        builder.ToTable("LicensePlateNumberSequences");
        builder.HasKey(sequence => sequence.Id);
        builder.Property(sequence => sequence.Prefix)
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(sequence => sequence.Padding).IsRequired();
        builder.Property(sequence => sequence.NextNumber).IsRequired();
        builder.Property(sequence => sequence.Revision)
            .IsRequired()
            .IsConcurrencyToken();
        builder.Property(sequence => sequence.CreatedAt)
            .IsRequired()
            .HasColumnType("timestamp with time zone");
        builder.Property(sequence => sequence.UpdatedAt)
            .HasColumnType("timestamp with time zone");

        builder.HasOne(sequence => sequence.Warehouse)
            .WithMany()
            .HasForeignKey(sequence => sequence.WarehouseId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(sequence => sequence.WarehouseId).IsUnique();
    }
}
