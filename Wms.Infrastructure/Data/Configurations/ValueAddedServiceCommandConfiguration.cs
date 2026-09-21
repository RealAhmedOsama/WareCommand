using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class ValueAddedServiceCommandConfiguration : IEntityTypeConfiguration<ValueAddedServiceCommand>
{
    public void Configure(EntityTypeBuilder<ValueAddedServiceCommand> builder)
    {
        builder.ToTable("ValueAddedServiceCommands");
        builder.HasKey(command => command.Id);
        builder.Property(command => command.Operation).HasMaxLength(80).IsRequired();
        builder.Property(command => command.IdempotencyKey).HasMaxLength(250).IsRequired();
        builder.Property(command => command.RequestHash).HasMaxLength(64).IsRequired();
        builder.Property(command => command.ActorUserId).HasMaxLength(450).IsRequired();
        builder.Property(command => command.OccurredAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasOne(command => command.Order)
            .WithMany()
            .HasForeignKey(command => command.ValueAddedServiceOrderId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(command => new
        {
            command.ValueAddedServiceOrderId,
            command.Operation,
            command.IdempotencyKey
        }).IsUnique();
    }
}
