using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class ReturnDispositionConfiguration : IEntityTypeConfiguration<ReturnDisposition>
{
    public void Configure(EntityTypeBuilder<ReturnDisposition> builder)
    {
        builder.ToTable("ReturnDispositions");
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Kind).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(value => value.Quantity).HasColumnType("numeric(28,12)");
        builder.Property(value => value.Reason).HasMaxLength(1000).IsRequired();
        builder.Property(value => value.UserId).HasMaxLength(450).IsRequired();
        builder.Property(value => value.DisposedAtUtc).HasColumnType("timestamp with time zone");
        builder.HasOne(value => value.ReturnAuthorization).WithMany(value => value.Dispositions).HasForeignKey(value => value.ReturnAuthorizationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(value => value.ReturnReceipt).WithMany().HasForeignKey(value => value.ReturnReceiptId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.DestinationLocation).WithMany().HasForeignKey(value => value.DestinationLocationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new { value.ReturnReceiptId, value.DisposedAtUtc });
    }
}
