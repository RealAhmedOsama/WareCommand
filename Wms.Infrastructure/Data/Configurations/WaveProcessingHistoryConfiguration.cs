using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class WaveProcessingHistoryConfiguration : IEntityTypeConfiguration<WaveProcessingHistory>
{
    public void Configure(EntityTypeBuilder<WaveProcessingHistory> builder)
    {
        builder.ToTable("WaveProcessingHistory");
        builder.HasKey(history => history.Id);
        builder.Property(history => history.Step).HasConversion<int>().IsRequired();
        builder.Property(history => history.IdempotencyKey).HasMaxLength(250).IsRequired();
        builder.Property(history => history.Status).HasConversion<int>().IsRequired();
        builder.Property(history => history.StartedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(history => history.CompletedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(history => history.ErrorMessage).HasMaxLength(2_000);
        builder.Property(history => history.DetailsJson).HasMaxLength(8_000);
        builder.Property(history => history.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(history => history.UpdatedAt).HasColumnType("timestamp with time zone");

        builder.HasOne(history => history.Wave)
            .WithMany(wave => wave.History)
            .HasForeignKey(history => history.WaveId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(history => new { history.WaveId, history.Step, history.Attempt }).IsUnique();
        builder.HasIndex(history => new { history.WaveId, history.IdempotencyKey });
    }
}
