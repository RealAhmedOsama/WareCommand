using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class WarehouseWorkCommandConfiguration : IEntityTypeConfiguration<WarehouseWorkCommand>
{
    public void Configure(EntityTypeBuilder<WarehouseWorkCommand> builder)
    {
        builder.ToTable("WarehouseWorkCommands");
        builder.HasKey(command => command.Id);
        builder.Property(command => command.Operation).HasMaxLength(100).IsRequired();
        builder.Property(command => command.IdempotencyKey).HasMaxLength(250).IsRequired();
        builder.Property(command => command.RequestHash).HasMaxLength(64).IsRequired();
        builder.Property(command => command.UserId).HasMaxLength(450).IsRequired();
        builder.Property(command => command.ExecutedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasOne(command => command.Work)
            .WithMany()
            .HasForeignKey(command => command.WarehouseWorkId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(command => new { command.WarehouseWorkId, command.Operation, command.IdempotencyKey }).IsUnique();
        builder.HasIndex(command => command.ExecutedAtUtc);
    }
}
