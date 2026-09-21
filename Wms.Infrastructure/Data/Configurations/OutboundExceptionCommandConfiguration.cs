using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class OutboundExceptionCommandConfiguration : IEntityTypeConfiguration<OutboundExceptionCommand>
{
    public void Configure(EntityTypeBuilder<OutboundExceptionCommand> builder)
    {
        builder.ToTable("OutboundExceptionCommands");
        builder.HasKey(command => command.Id);
        builder.Property(command => command.Operation).IsRequired().HasMaxLength(100);
        builder.Property(command => command.IdempotencyKey).IsRequired().HasMaxLength(250);
        builder.Property(command => command.RequestHash).IsRequired().HasMaxLength(64);
        builder.Property(command => command.UserId).IsRequired().HasMaxLength(450);
        builder.Property(command => command.ExecutedAtUtc).IsRequired().HasColumnType("timestamp with time zone");
        builder.HasOne(command => command.OutboundException)
            .WithMany()
            .HasForeignKey(command => command.OutboundExceptionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(command => new
        {
            command.OutboundExceptionId,
            command.Operation,
            command.IdempotencyKey
        }).IsUnique();
    }
}
