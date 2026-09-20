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

        builder.Property(e => e.LocalizedName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.Description)
            .HasMaxLength(1000);

        builder.Property(e => e.LocalizedDescription)
            .IsRequired()
            .HasMaxLength(1000);

        builder.Property(e => e.Category)
            .HasMaxLength(100);

        builder.Property(e => e.Brand)
            .HasMaxLength(100);

        builder.Property(e => e.Type)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(e => e.LifecycleStatus)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(e => e.ImageReference)
            .HasMaxLength(500);

        builder.Property(e => e.DocumentReference)
            .HasMaxLength(500);

        builder.Property(e => e.UnitOfMeasure)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(e => e.PurchaseUnit)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(e => e.SalesUnit)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(e => e.AllowFractionalQuantity)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(e => e.NetWeightKg).HasColumnType("decimal(18,6)");
        builder.Property(e => e.LengthCm).HasColumnType("decimal(18,6)");
        builder.Property(e => e.WidthCm).HasColumnType("decimal(18,6)");
        builder.Property(e => e.HeightCm).HasColumnType("decimal(18,6)");
        builder.Property(e => e.VolumeCubicMeters).HasColumnType("decimal(18,9)");
        builder.Property(e => e.StandardCost).HasColumnType("decimal(18,4)");
        builder.Property(e => e.SalesPrice).HasColumnType("decimal(18,4)");
        builder.Property(e => e.MinimumTemperatureCelsius).HasColumnType("decimal(18,4)");
        builder.Property(e => e.MaximumTemperatureCelsius).HasColumnType("decimal(18,4)");
        builder.Property(e => e.StorageProfile).HasMaxLength(100);
        builder.Property(e => e.PutawayProfile).HasMaxLength(100);
        builder.Property(e => e.DefaultSupplierCode).HasMaxLength(100);
        builder.Property(e => e.ReorderPolicy)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(e => e.MinimumStock).HasColumnType("decimal(18,4)");
        builder.Property(e => e.MaximumStock).HasColumnType("decimal(18,4)");
        builder.Property(e => e.SafetyStock).HasColumnType("decimal(18,4)");

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
            b.HasIndex(bc => bc.Value).IsUnique();
        });

        // Indexes
        builder.HasIndex(e => e.Sku).IsUnique();
        builder.HasIndex(e => e.IsActive);
    }
}
