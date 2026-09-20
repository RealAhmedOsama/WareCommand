using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class InventoryCommandIdempotencyConfiguration
    : IEntityTypeConfiguration<InventoryCommandIdempotency>
{
    public void Configure(EntityTypeBuilder<InventoryCommandIdempotency> builder)
    {
        builder.ToTable("InventoryCommandIdempotencies");
        builder.HasKey(command => command.Id);

        builder.Property(command => command.CommandKey)
            .HasMaxLength(250)
            .IsRequired();
        builder.Property(command => command.OperationType)
            .HasMaxLength(100)
            .IsRequired();
        builder.Property(command => command.CallerScope)
            .HasMaxLength(250)
            .IsRequired();
        builder.Property(command => command.RequestHash)
            .HasMaxLength(64)
            .IsRequired();
        builder.Property(command => command.Status)
            .HasConversion<int>()
            .IsRequired();
        builder.Property(command => command.ResultType)
            .HasMaxLength(200);
        builder.Property(command => command.ResultPayloadJson)
            .HasMaxLength(32_000);
        builder.Property(command => command.ResultReference)
            .HasMaxLength(200);
        builder.Property(command => command.FailureCode)
            .HasMaxLength(200);
        builder.Property(command => command.FailureMessage)
            .HasMaxLength(2_000);
        builder.Property(command => command.CorrelationId)
            .HasMaxLength(100)
            .IsRequired();
        builder.Property(command => command.ActorUserId)
            .HasMaxLength(450);
        builder.Property(command => command.StartedAtUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(command => command.CompletedAtUtc)
            .HasColumnType("timestamp with time zone");
        builder.Property(command => command.ExpiresAtUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(command => command.Revision)
            .IsRequired()
            .IsConcurrencyToken();
        builder.Property(command => command.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(command => command.UpdatedAt)
            .HasColumnType("timestamp with time zone");

        builder.HasIndex(command => new
        {
            command.CallerScope,
            command.CommandKey
        }).IsUnique();
        builder.HasIndex(command => new
        {
            command.Status,
            command.ExpiresAtUtc
        });
        builder.HasIndex(command => new
        {
            command.OperationType,
            command.CreatedAt
        });
    }
}
