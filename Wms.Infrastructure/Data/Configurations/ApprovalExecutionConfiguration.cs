using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class ApprovalExecutionConfiguration : IEntityTypeConfiguration<ApprovalExecution>
{
    public void Configure(EntityTypeBuilder<ApprovalExecution> builder)
    {
        builder.ToTable("ApprovalExecutions");
        builder.HasKey(execution => execution.Id);
        builder.Property(execution => execution.IdempotencyKey).HasMaxLength(250).IsRequired();
        builder.Property(execution => execution.CurrentStateHash).HasMaxLength(128).IsRequired();
        builder.Property(execution => execution.StartedByUserId).HasMaxLength(450).IsRequired();
        builder.Property(execution => execution.Status).HasConversion<int>().IsRequired();
        builder.Property(execution => execution.StartedAtUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(execution => execution.CompletedAtUtc)
            .HasColumnType("timestamp with time zone");
        builder.Property(execution => execution.ResultReference).HasMaxLength(250);
        builder.Property(execution => execution.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(execution => execution.UpdatedAt)
            .HasColumnType("timestamp with time zone");

        builder.HasOne<ApprovalRequest>()
            .WithMany()
            .HasForeignKey(execution => execution.ApprovalRequestId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(execution => execution.ApprovalRequestId).IsUnique();
        builder.HasIndex(execution => execution.IdempotencyKey).IsUnique();
    }
}
