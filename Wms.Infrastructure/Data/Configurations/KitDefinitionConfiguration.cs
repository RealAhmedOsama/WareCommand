using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class KitDefinitionConfiguration : IEntityTypeConfiguration<KitDefinition>
{
    public void Configure(EntityTypeBuilder<KitDefinition> builder)
    {
        builder.ToTable("KitDefinitions");
        builder.HasKey(definition => definition.Id);
        builder.Property(definition => definition.Code).HasMaxLength(80).IsRequired();
        builder.Property(definition => definition.OutputUnitOfMeasure).HasMaxLength(20).IsRequired();
        builder.Property(definition => definition.Instructions).HasMaxLength(8_000);
        builder.Property(definition => definition.LocalizedInstructions).HasMaxLength(8_000);
        builder.Property(definition => definition.Revision).IsRequired().IsConcurrencyToken();
        builder.Property(definition => definition.EffectiveFromUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(definition => definition.EffectiveToUtc)
            .HasColumnType("timestamp with time zone");

        builder.HasOne(definition => definition.OutputItem)
            .WithMany()
            .HasForeignKey(definition => definition.OutputItemId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(definition => definition.Lines)
            .WithOne(line => line.Definition)
            .HasForeignKey(line => line.KitDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(definition => new { definition.Code, definition.Version }).IsUnique();
        builder.HasIndex(definition => new { definition.OutputItemId, definition.IsActive });
    }
}
