using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class ReturnCommandConfiguration : IEntityTypeConfiguration<ReturnCommand>
{
    public void Configure(EntityTypeBuilder<ReturnCommand> builder)
    {
        builder.ToTable("ReturnCommands");
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Operation).HasMaxLength(100).IsRequired();
        builder.Property(value => value.IdempotencyKey).HasMaxLength(250).IsRequired();
        builder.Property(value => value.RequestHash).HasMaxLength(64).IsRequired();
        builder.Property(value => value.UserId).HasMaxLength(450).IsRequired();
        builder.Property(value => value.ExecutedAtUtc).HasColumnType("timestamp with time zone");
        builder.HasOne(value => value.ReturnAuthorization).WithMany().HasForeignKey(value => value.ReturnAuthorizationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(value => new { value.ReturnAuthorizationId, value.Operation, value.IdempotencyKey }).IsUnique();
    }
}
