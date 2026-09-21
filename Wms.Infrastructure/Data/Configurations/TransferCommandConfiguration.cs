using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class TransferCommandConfiguration : IEntityTypeConfiguration<TransferCommand>
{
    public void Configure(EntityTypeBuilder<TransferCommand> builder)
    {
        builder.ToTable("TransferCommands");
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Operation).HasMaxLength(100).IsRequired();
        builder.Property(value => value.IdempotencyKey).HasMaxLength(250).IsRequired();
        builder.Property(value => value.RequestHash).HasMaxLength(64).IsRequired();
        builder.Property(value => value.UserId).HasMaxLength(450).IsRequired();
        builder.Property(value => value.ExecutedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasOne(value => value.TransferOrder).WithMany().HasForeignKey(value => value.TransferOrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(value => new { value.TransferOrderId, value.Operation, value.IdempotencyKey }).IsUnique();
    }
}
