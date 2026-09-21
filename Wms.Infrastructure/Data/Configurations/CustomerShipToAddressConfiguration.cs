using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class CustomerShipToAddressConfiguration : IEntityTypeConfiguration<CustomerShipToAddress>
{
    public void Configure(EntityTypeBuilder<CustomerShipToAddress> builder)
    {
        builder.ToTable("CustomerShipToAddresses");
        builder.HasKey(address => address.Id);

        builder.Property(address => address.Code).IsRequired().HasMaxLength(50);
        builder.Property(address => address.RecipientName).IsRequired().HasMaxLength(200);
        builder.Property(address => address.Phone).HasMaxLength(50);
        builder.Property(address => address.CountryCode).HasMaxLength(2);
        builder.Property(address => address.Region).HasMaxLength(100);
        builder.Property(address => address.City).HasMaxLength(100);
        builder.Property(address => address.PostalCode).HasMaxLength(30);
        builder.Property(address => address.AddressLine1).IsRequired().HasMaxLength(200);
        builder.Property(address => address.AddressLine2).HasMaxLength(200);
        builder.Property(address => address.DeliveryInstructions).HasMaxLength(1_000);
        builder.Property(address => address.DeliveryWindowStart).HasColumnType("time");
        builder.Property(address => address.DeliveryWindowEnd).HasColumnType("time");
        builder.Property(address => address.IsDefault).IsRequired();
        builder.Property(address => address.IsActive).IsRequired();
        builder.Property(address => address.CreatedAt).IsRequired().HasColumnType("timestamp with time zone");
        builder.Property(address => address.UpdatedAt).HasColumnType("timestamp with time zone");

        builder.HasIndex(address => new { address.CustomerId, address.Code }).IsUnique();
        builder.HasIndex(address => new { address.CustomerId, address.IsActive, address.IsDefault });
    }
}
