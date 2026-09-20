using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.ToTable("Suppliers");
        builder.HasKey(supplier => supplier.Id);

        builder.Property(supplier => supplier.Code)
            .IsRequired()
            .HasMaxLength(50);
        builder.Property(supplier => supplier.LegalName)
            .IsRequired()
            .HasMaxLength(200);
        builder.Property(supplier => supplier.LocalizedName).HasMaxLength(200);
        builder.Property(supplier => supplier.TaxRegistrationNumber).HasMaxLength(100);
        builder.Property(supplier => supplier.ExternalErpIdentifier).HasMaxLength(100);
        builder.Property(supplier => supplier.AddressLine1).HasMaxLength(200);
        builder.Property(supplier => supplier.AddressLine2).HasMaxLength(200);
        builder.Property(supplier => supplier.City).HasMaxLength(100);
        builder.Property(supplier => supplier.StateOrProvince).HasMaxLength(100);
        builder.Property(supplier => supplier.PostalCode).HasMaxLength(30);
        builder.Property(supplier => supplier.CountryCode).HasMaxLength(2);
        builder.Property(supplier => supplier.ContactName).HasMaxLength(200);
        builder.Property(supplier => supplier.ContactEmail).HasMaxLength(320);
        builder.Property(supplier => supplier.ContactPhone).HasMaxLength(50);
        builder.Property(supplier => supplier.QualityProfile).HasMaxLength(100);
        builder.Property(supplier => supplier.LabelRule).HasMaxLength(100);
        builder.Property(supplier => supplier.DefaultCurrencyCode).HasMaxLength(3);
        builder.Property(supplier => supplier.Notes).HasMaxLength(2_000);
        builder.Property(supplier => supplier.OverDeliveryTolerancePercent)
            .HasColumnType("decimal(18,4)");
        builder.Property(supplier => supplier.UnderDeliveryTolerancePercent)
            .HasColumnType("decimal(18,4)");
        builder.Property(supplier => supplier.IsActive).IsRequired();
        builder.Property(supplier => supplier.CreatedAt)
            .IsRequired()
            .HasColumnType("timestamp with time zone");
        builder.Property(supplier => supplier.UpdatedAt)
            .HasColumnType("timestamp with time zone");

        builder.HasMany(supplier => supplier.ItemReferences)
            .WithOne(reference => reference.Supplier)
            .HasForeignKey(reference => reference.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Warehouse>()
            .WithMany()
            .HasForeignKey(supplier => supplier.PreferredWarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Location>()
            .WithMany()
            .HasForeignKey(supplier => supplier.PreferredDockLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(supplier => supplier.Code).IsUnique();
        builder.HasIndex(supplier => supplier.ExternalErpIdentifier).IsUnique();
        builder.HasIndex(supplier => supplier.IsActive);
        builder.HasIndex(supplier => new { supplier.PreferredWarehouseId, supplier.IsActive });
        builder.HasIndex(supplier => supplier.LegalName);
    }
}
