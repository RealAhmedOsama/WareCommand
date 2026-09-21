using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class ApprovalDecisionConfiguration : IEntityTypeConfiguration<ApprovalDecision>
{
    public void Configure(EntityTypeBuilder<ApprovalDecision> builder)
    {
        builder.ToTable("ApprovalDecisions");
        builder.HasKey(decision => decision.Id);
        builder.Property(decision => decision.IdempotencyKey).HasMaxLength(250).IsRequired();
        builder.Property(decision => decision.ActorUserId).HasMaxLength(450).IsRequired();
        builder.Property(decision => decision.ActorRoleSnapshotJson).HasMaxLength(4_000).IsRequired();
        builder.Property(decision => decision.Comment).HasMaxLength(2_000);
        builder.Property(decision => decision.Type).HasConversion<int>().IsRequired();
        builder.Property(decision => decision.OccurredAtUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(decision => decision.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(decision => decision.UpdatedAt)
            .HasColumnType("timestamp with time zone");

        builder.HasOne<ApprovalRequest>()
            .WithMany()
            .HasForeignKey(decision => decision.ApprovalRequestId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(decision => new { decision.ApprovalRequestId, decision.IdempotencyKey }).IsUnique();
        builder.HasIndex(decision => new { decision.ApprovalRequestId, decision.Level, decision.OccurredAtUtc });
    }
}
