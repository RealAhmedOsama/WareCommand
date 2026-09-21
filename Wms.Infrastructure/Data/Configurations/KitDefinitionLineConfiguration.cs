using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class KitDefinitionLineConfiguration : IEntityTypeConfiguration<KitDefinitionLine>
{
    public void Configure(EntityTypeBuilder<KitDefinitionLine> builder)
    {
        builder.ToTable("KitDefinitionLines");
        builder.HasKey(line => line.Id);
        builder.Property(line => line.QuantityPerOutput).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(line => line.ComponentUnitOfMeasure).HasMaxLength(20).IsRequired();
        builder.Property(line => line.SubstitutionPolicy).HasConversion<int>().IsRequired();
        builder.Property(line => line.ApprovedSubstitutionItemIdsJson).HasMaxLength(2_000);
        builder.Property(line => line.Notes).HasMaxLength(1_000);
        builder.Property(line => line.Revision).IsRequired().IsConcurrencyToken();

        builder.HasOne(line => line.ComponentItem)
            .WithMany()
            .HasForeignKey(line => line.ComponentItemId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(line => new { line.KitDefinitionId, line.Sequence }).IsUnique();
        builder.HasIndex(line => line.ComponentItemId);
    }
}
