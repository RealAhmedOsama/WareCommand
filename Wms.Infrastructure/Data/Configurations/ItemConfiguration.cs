// Wms.Infrastructure/Data/Configurations/ItemConfiguration.cs

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public class ItemConfiguration : IEntityTypeConfiguration<Item>
{
    public void Configure(EntityTypeBuilder<Item> builder)
    {
        builder.ToTable("Items");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Sku)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.Description)
            .HasMaxLength(1000);

        builder.Property(e => e.UnitOfMeasure)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(e => e.CreatedAt)
            .IsRequired()
            .HasColumnType("timestamp with time zone");

        builder.Property(e => e.UpdatedAt)
            .HasColumnType("timestamp with time zone");

        // Configure barcodes as owned entities
        builder.OwnsMany(e => e.Barcodes, b =>
        {
            b.ToTable("ItemBarcodes");
            b.WithOwner().HasForeignKey("ItemId");
            b.Property<int>("Id").ValueGeneratedOnAdd();
            b.HasKey("Id");
            b.Property(bc => bc.Value)
                .HasColumnName("Barcode")
                .IsRequired()
                .HasMaxLength(50);
        });

        // Indexes
        builder.HasIndex(e => e.Sku).IsUnique();
        builder.HasIndex(e => e.IsActive);
    }
}
