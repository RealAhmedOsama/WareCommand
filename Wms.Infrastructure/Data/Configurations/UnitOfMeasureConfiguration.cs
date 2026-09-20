using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class UnitOfMeasureConfiguration : IEntityTypeConfiguration<UnitOfMeasure>
{
    public void Configure(EntityTypeBuilder<UnitOfMeasure> builder)
    {
        builder.ToTable("UnitOfMeasures");
        builder.HasKey(unit => unit.Id);

        builder.Property(unit => unit.Code)
            .IsRequired()
            .HasMaxLength(20);
        builder.Property(unit => unit.Category)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(unit => unit.Precision)
            .IsRequired();
        builder.Property(unit => unit.Symbol)
            .IsRequired()
            .HasMaxLength(20);
        builder.Property(unit => unit.Name)
            .IsRequired()
            .HasMaxLength(200);
        builder.Property(unit => unit.LocalizedName)
            .IsRequired()
            .HasMaxLength(200);
        builder.Property(unit => unit.IsActive)
            .IsRequired();
        builder.Property(unit => unit.CreatedAt)
            .IsRequired()
            .HasColumnType("timestamp with time zone");
        builder.Property(unit => unit.UpdatedAt)
            .HasColumnType("timestamp with time zone");

        builder.HasIndex(unit => unit.Code).IsUnique();
        builder.HasIndex(unit => new { unit.Category, unit.IsActive });
    }
}
