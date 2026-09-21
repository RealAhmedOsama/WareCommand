using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class ApprovalRequestConfiguration : IEntityTypeConfiguration<ApprovalRequest>
{
    public void Configure(EntityTypeBuilder<ApprovalRequest> builder)
    {
        builder.ToTable("ApprovalRequests");
        builder.HasKey(request => request.Id);
        builder.Property(request => request.RequestIdempotencyKey).HasMaxLength(250).IsRequired();
        builder.Property(request => request.Module).HasMaxLength(100).IsRequired();
        builder.Property(request => request.Operation).HasMaxLength(150).IsRequired();
        builder.Property(request => request.ReasonCode).HasMaxLength(80).IsRequired();
        builder.Property(request => request.PolicyCode).HasMaxLength(80);
        builder.Property(request => request.SourceEntityType).HasMaxLength(100).IsRequired();
        builder.Property(request => request.SourceEntityId).HasMaxLength(200).IsRequired();
        builder.Property(request => request.SourceReference).HasMaxLength(250);
        builder.Property(request => request.RequesterUserId).HasMaxLength(450).IsRequired();
        builder.Property(request => request.RequiredPermission).HasMaxLength(100).IsRequired();
        builder.Property(request => request.Quantity).HasColumnType("decimal(18,4)");
        builder.Property(request => request.Value).HasColumnType("decimal(18,4)");
        builder.Property(request => request.VariancePercent).HasColumnType("decimal(18,4)");
        builder.Property(request => request.ItemRisk).HasMaxLength(50);
        builder.Property(request => request.StatusRisk).HasMaxLength(50);
        builder.Property(request => request.CurrentStateHash).HasMaxLength(128).IsRequired();
        builder.Property(request => request.Notes).HasMaxLength(2_000);
        builder.Property(request => request.AttachmentReference).HasMaxLength(500);
        builder.Property(request => request.ApprovalLevelsJson).HasMaxLength(4_000).IsRequired();
        builder.Property(request => request.Status).HasConversion<int>().IsRequired();
        builder.Property(request => request.RequestedAtUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(request => request.ExpiresAtUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(request => request.DecidedAtUtc)
            .HasColumnType("timestamp with time zone");
        builder.Property(request => request.ExecutingAtUtc)
            .HasColumnType("timestamp with time zone");
        builder.Property(request => request.ExecutedAtUtc)
            .HasColumnType("timestamp with time zone");
        builder.Property(request => request.LastComment).HasMaxLength(2_000);
        builder.Property(request => request.Revision).IsRequired().IsConcurrencyToken();
        builder.Property(request => request.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(request => request.UpdatedAt)
            .HasColumnType("timestamp with time zone");

        builder.HasOne<ReasonCode>()
            .WithMany()
            .HasForeignKey(request => request.ReasonCodeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApprovalPolicy>()
            .WithMany()
            .HasForeignKey(request => request.PolicyId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(request => request.RequestIdempotencyKey).IsUnique();
        builder.HasIndex(request => new { request.Status, request.WarehouseId, request.ExpiresAtUtc });
        builder.HasIndex(request => new { request.SourceEntityType, request.SourceEntityId });
    }
}
