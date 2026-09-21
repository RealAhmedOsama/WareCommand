using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class InventoryOwnerConfiguration : IEntityTypeConfiguration<InventoryOwner>
{
    public void Configure(EntityTypeBuilder<InventoryOwner> builder)
    {
        builder.ToTable("InventoryOwners", table => table.HasCheckConstraint(
            "CK_InventoryOwners_LinkedOwner",
            "(\"Kind\" = 2 AND \"SupplierId\" IS NOT NULL AND \"CustomerId\" IS NULL AND \"ExternalOwnerReference\" IS NULL) OR " +
            "(\"Kind\" = 3 AND \"SupplierId\" IS NULL AND \"CustomerId\" IS NOT NULL AND \"ExternalOwnerReference\" IS NULL) OR " +
            "(\"Kind\" = 4 AND \"SupplierId\" IS NULL AND \"CustomerId\" IS NULL AND \"ExternalOwnerReference\" IS NOT NULL)"));
        builder.HasKey(owner => owner.Id);
        builder.Property(owner => owner.OwnerCode).HasMaxLength(80).IsRequired();
        builder.Property(owner => owner.Kind).HasConversion<int>().IsRequired();
        builder.Property(owner => owner.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(owner => owner.LocalizedName).HasMaxLength(200);
        builder.Property(owner => owner.ExternalOwnerReference).HasMaxLength(120);
        builder.Property(owner => owner.Revision).IsRequired().IsConcurrencyToken();
        builder.Property(owner => owner.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(owner => owner.UpdatedAt).HasColumnType("timestamp with time zone");

        builder.HasOne(owner => owner.Supplier)
            .WithMany()
            .HasForeignKey(owner => owner.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(owner => owner.Customer)
            .WithMany()
            .HasForeignKey(owner => owner.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(owner => owner.OwnerCode).IsUnique();
        builder.HasIndex(owner => new { owner.Kind, owner.SupplierId, owner.CustomerId, owner.ExternalOwnerReference })
            .IsUnique()
            .AreNullsDistinct(false);
    }
}
