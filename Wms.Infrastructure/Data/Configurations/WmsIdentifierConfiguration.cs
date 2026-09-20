using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class WmsIdentifierConfiguration : IEntityTypeConfiguration<WmsIdentifier>
{
    public void Configure(EntityTypeBuilder<WmsIdentifier> builder)
    {
        builder.ToTable("WmsIdentifiers");
        builder.HasKey(identifier => identifier.Id);

        builder.Property(identifier => identifier.OriginalValue)
            .IsRequired()
            .HasMaxLength(200);
        builder.Property(identifier => identifier.NormalizedValue)
            .IsRequired()
            .HasMaxLength(200);
        builder.Property(identifier => identifier.Kind)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(identifier => identifier.Symbology)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(identifier => identifier.OwnerKey)
            .IsRequired()
            .HasMaxLength(200);
        builder.Property(identifier => identifier.Alias)
            .HasMaxLength(100);
        builder.Property(identifier => identifier.ValidFromUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(identifier => identifier.ValidToUtc)
            .HasColumnType("timestamp with time zone");
        builder.Property(identifier => identifier.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(identifier => identifier.UpdatedAt)
            .HasColumnType("timestamp with time zone");

        builder.HasIndex(identifier => identifier.NormalizedValue)
            .IsUnique();
        builder.HasIndex(identifier => new
        {
            identifier.OwnerKey,
            identifier.Kind,
            identifier.IsActive
        });
        builder.HasIndex(identifier => new
        {
            identifier.IsActive,
            identifier.ValidFromUtc,
            identifier.ValidToUtc
        });
    }
}
