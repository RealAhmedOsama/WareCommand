using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class ReceiptLineLinkConfiguration : IEntityTypeConfiguration<ReceiptLineLink>
{
    public void Configure(EntityTypeBuilder<ReceiptLineLink> builder)
    {
        builder.ToTable("ReceiptLineLinks");
        builder.HasKey(link => link.Id);
        builder.Property(link => link.Type).HasConversion<int>().IsRequired();
        builder.Property(link => link.Reference).HasMaxLength(200).IsRequired();
        builder.Property(link => link.UserId).HasMaxLength(450).IsRequired();
        builder.Property(link => link.Notes).HasMaxLength(1_000);
        builder.Property(link => link.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(link => link.UpdatedAt).HasColumnType("timestamp with time zone");

        builder.HasOne(link => link.ReceiptLine)
            .WithMany(line => line.Links)
            .HasForeignKey(link => link.ReceiptLineId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(link => new { link.ReceiptLineId, link.Type, link.Reference }).IsUnique();
    }
}
