using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customers");
        builder.HasKey(customer => customer.Id);

        builder.Property(customer => customer.Code).IsRequired().HasMaxLength(50);
        builder.Property(customer => customer.LegalName).IsRequired().HasMaxLength(200);
        builder.Property(customer => customer.LocalizedName).HasMaxLength(200);
        builder.Property(customer => customer.TaxRegistrationNumber).HasMaxLength(100);
        builder.Property(customer => customer.ExternalErpIdentifier).HasMaxLength(100);
        builder.Property(customer => customer.ExternalChannelIdentifier).HasMaxLength(100);
        builder.Property(customer => customer.ContactName).HasMaxLength(200);
        builder.Property(customer => customer.ContactEmail).HasMaxLength(320);
        builder.Property(customer => customer.ContactPhone).HasMaxLength(50);
        builder.Property(customer => customer.BillingAddressLine1).HasMaxLength(200);
        builder.Property(customer => customer.BillingAddressLine2).HasMaxLength(200);
        builder.Property(customer => customer.BillingCity).HasMaxLength(100);
        builder.Property(customer => customer.BillingRegion).HasMaxLength(100);
        builder.Property(customer => customer.BillingPostalCode).HasMaxLength(30);
        builder.Property(customer => customer.BillingCountryCode).HasMaxLength(2);
        builder.Property(customer => customer.DefaultCarrierCode).HasMaxLength(50);
        builder.Property(customer => customer.DefaultCarrierServiceCode).HasMaxLength(80);
        builder.Property(customer => customer.Priority).IsRequired();
        builder.Property(customer => customer.PackagingProfile).HasMaxLength(100);
        builder.Property(customer => customer.LabelProfile).HasMaxLength(100);
        builder.Property(customer => customer.AllowPartialShipment).IsRequired();
        builder.Property(customer => customer.Notes).HasMaxLength(2_000);
        builder.Property(customer => customer.IsActive).IsRequired();
        builder.Property(customer => customer.CreatedAt).IsRequired().HasColumnType("timestamp with time zone");
        builder.Property(customer => customer.UpdatedAt).HasColumnType("timestamp with time zone");

        builder.HasMany(customer => customer.ShipToAddresses)
            .WithOne(address => address.Customer)
            .HasForeignKey(address => address.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(customer => customer.ItemReferences)
            .WithOne(reference => reference.Customer)
            .HasForeignKey(reference => reference.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(customer => customer.Code).IsUnique();
        builder.HasIndex(customer => customer.ExternalErpIdentifier).IsUnique();
        builder.HasIndex(customer => customer.ExternalChannelIdentifier).IsUnique();
        builder.HasIndex(customer => customer.IsActive);
        builder.HasIndex(customer => new { customer.Priority, customer.IsActive });
        builder.HasIndex(customer => customer.LegalName);
    }
}
