using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class CustomerItemReferenceConfiguration : IEntityTypeConfiguration<CustomerItemReference>
{
    public void Configure(EntityTypeBuilder<CustomerItemReference> builder)
    {
        builder.ToTable("CustomerItemReferences");
        builder.HasKey(reference => reference.Id);

        builder.Property(reference => reference.CustomerSku).IsRequired().HasMaxLength(100);
        builder.Property(reference => reference.CustomerBarcode).HasMaxLength(100);
        builder.Property(reference => reference.CustomerDescription).HasMaxLength(200);
        builder.Property(reference => reference.IsActive).IsRequired();
        builder.Property(reference => reference.CreatedAt).IsRequired().HasColumnType("timestamp with time zone");
        builder.Property(reference => reference.UpdatedAt).HasColumnType("timestamp with time zone");

        builder.HasOne(reference => reference.Item)
            .WithMany()
            .HasForeignKey(reference => reference.ItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(reference => new { reference.CustomerId, reference.CustomerSku }).IsUnique();
        builder.HasIndex(reference => new { reference.CustomerId, reference.CustomerBarcode }).IsUnique();
        builder.HasIndex(reference => new { reference.ItemId, reference.IsActive });
        builder.HasIndex(reference => new { reference.CustomerId, reference.IsActive });
    }
}
