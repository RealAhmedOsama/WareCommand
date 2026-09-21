using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class PackingCommandConfiguration : IEntityTypeConfiguration<PackingCommand>
{
    public void Configure(EntityTypeBuilder<PackingCommand> builder)
    {
        builder.ToTable("PackingCommands");
        builder.HasKey(command => command.Id);
        builder.Property(command => command.Operation).IsRequired().HasMaxLength(100);
        builder.Property(command => command.IdempotencyKey).IsRequired().HasMaxLength(250);
        builder.Property(command => command.RequestHash).IsRequired().HasMaxLength(64);
        builder.Property(command => command.UserId).IsRequired().HasMaxLength(450);
        builder.Property(command => command.ExecutedAtUtc).IsRequired().HasColumnType("timestamp with time zone");
        builder.HasOne(command => command.PackingSession)
            .WithMany()
            .HasForeignKey(command => command.PackingSessionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(command => command.ShipmentPackage)
            .WithMany()
            .HasForeignKey(command => command.ShipmentPackageId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(command => new { command.PackingSessionId, command.Operation, command.IdempotencyKey })
            .IsUnique();
        builder.HasIndex(command => new { command.ShipmentPackageId, command.Operation, command.IdempotencyKey })
            .IsUnique();
        builder.HasIndex(command => command.ExecutedAtUtc);
    }
}
