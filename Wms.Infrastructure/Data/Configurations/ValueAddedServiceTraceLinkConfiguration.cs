using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class ValueAddedServiceTraceLinkConfiguration : IEntityTypeConfiguration<ValueAddedServiceTraceLink>
{
    public void Configure(EntityTypeBuilder<ValueAddedServiceTraceLink> builder)
    {
        builder.ToTable("ValueAddedServiceTraceLinks");
        builder.HasKey(link => link.Id);
        builder.Property(link => link.Kind).HasConversion<int>().IsRequired();
        builder.Property(link => link.Quantity).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(link => link.ReversedQuantity).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(link => link.IdempotencyKey).HasMaxLength(250).IsRequired();
        builder.Property(link => link.OccurredAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasOne(link => link.InputLine)
            .WithMany()
            .HasForeignKey(link => link.InputLineId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(link => link.OutputLine)
            .WithMany()
            .HasForeignKey(link => link.OutputLineId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(link => new { link.OrderId, link.IdempotencyKey }).IsUnique();
        builder.HasIndex(link => new { link.InputLineId, link.OutputLineId });
    }
}
