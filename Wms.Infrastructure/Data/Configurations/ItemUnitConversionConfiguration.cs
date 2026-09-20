using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class ItemUnitConversionConfiguration : IEntityTypeConfiguration<ItemUnitConversion>
{
    public void Configure(EntityTypeBuilder<ItemUnitConversion> builder)
    {
        builder.ToTable("ItemUnitConversions");
        builder.HasKey(conversion => conversion.Id);

        builder.Property(conversion => conversion.FromUnitOfMeasure)
            .IsRequired()
            .HasMaxLength(20);
        builder.Property(conversion => conversion.ToUnitOfMeasure)
            .IsRequired()
            .HasMaxLength(20);
        builder.Property(conversion => conversion.ConversionFactor)
            .HasColumnType("decimal(28,12)")
            .IsRequired();
        builder.Property(conversion => conversion.ResultPrecision)
            .IsRequired();
        builder.Property(conversion => conversion.RoundingMode)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(conversion => conversion.Version)
            .IsRequired();
        builder.Property(conversion => conversion.IsActive)
            .IsRequired();
        builder.Property(conversion => conversion.CreatedAt)
            .IsRequired()
            .HasColumnType("timestamp with time zone");
        builder.Property(conversion => conversion.UpdatedAt)
            .HasColumnType("timestamp with time zone");

        builder.HasOne(conversion => conversion.Item)
            .WithMany(item => item.UnitConversions)
            .HasForeignKey(conversion => conversion.ItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(conversion => new
        {
            conversion.ItemId,
            conversion.FromUnitOfMeasure,
            conversion.ToUnitOfMeasure,
            conversion.Version
        }).IsUnique();
        builder.HasIndex(conversion => new
        {
            conversion.ItemId,
            conversion.FromUnitOfMeasure,
            conversion.ToUnitOfMeasure,
            conversion.IsActive
        });
    }
}
