using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class SupplierItemReferenceConfiguration : IEntityTypeConfiguration<SupplierItemReference>
{
    public void Configure(EntityTypeBuilder<SupplierItemReference> builder)
    {
        builder.ToTable("SupplierItemReferences");
        builder.HasKey(reference => reference.Id);

        builder.Property(reference => reference.VendorSku)
            .IsRequired()
            .HasMaxLength(100);
        builder.Property(reference => reference.VendorBarcode).HasMaxLength(100);
        builder.Property(reference => reference.VendorPackaging).HasMaxLength(100);
        builder.Property(reference => reference.UnitsPerPurchasePackage)
            .HasColumnType("decimal(28,12)");
        builder.Property(reference => reference.MinimumOrderQuantity)
            .HasColumnType("decimal(28,12)")
            .IsRequired();
        builder.Property(reference => reference.IsActive).IsRequired();
        builder.Property(reference => reference.CreatedAt)
            .IsRequired()
            .HasColumnType("timestamp with time zone");
        builder.Property(reference => reference.UpdatedAt)
            .HasColumnType("timestamp with time zone");

        builder.HasOne(reference => reference.Item)
            .WithMany()
            .HasForeignKey(reference => reference.ItemId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(reference => reference.ItemPackaging)
            .WithMany()
            .HasForeignKey(reference => reference.ItemPackagingId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(reference => new { reference.SupplierId, reference.VendorSku })
            .IsUnique();
        builder.HasIndex(reference => new { reference.SupplierId, reference.VendorBarcode })
            .IsUnique();
        builder.HasIndex(reference => new { reference.ItemId, reference.IsActive });
        builder.HasIndex(reference => new { reference.SupplierId, reference.IsActive });
    }
}
