using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class ApprovalInboxItemConfiguration : IEntityTypeConfiguration<ApprovalInboxItem>
{
    public void Configure(EntityTypeBuilder<ApprovalInboxItem> builder)
    {
        builder.ToTable("ApprovalInboxItems");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.RecipientUserId).HasMaxLength(450).IsRequired();
        builder.Property(item => item.DeduplicationKey).HasMaxLength(250).IsRequired();
        builder.Property(item => item.Title).HasMaxLength(200).IsRequired();
        builder.Property(item => item.Message).HasMaxLength(2_000).IsRequired();
        builder.Property(item => item.CreatedAtUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(item => item.ExpiresAtUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(item => item.ReadAtUtc)
            .HasColumnType("timestamp with time zone");
        builder.Property(item => item.ResolvedAtUtc)
            .HasColumnType("timestamp with time zone");
        builder.Property(item => item.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(item => item.UpdatedAt)
            .HasColumnType("timestamp with time zone");

        builder.HasOne<ApprovalRequest>()
            .WithMany()
            .HasForeignKey(item => item.ApprovalRequestId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(item => item.DeduplicationKey).IsUnique();
        builder.HasIndex(item => new { item.RecipientUserId, item.ResolvedAtUtc, item.CreatedAtUtc });
        builder.HasIndex(item => new { item.ApprovalRequestId, item.Level });
    }
}
