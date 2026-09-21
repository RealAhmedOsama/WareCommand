using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Infrastructure.Integrations;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class WmsIntegrationOutboxConfiguration : IEntityTypeConfiguration<WmsIntegrationOutboxEntity>
{
    public void Configure(EntityTypeBuilder<WmsIntegrationOutboxEntity> builder)
    {
        builder.ToTable("WmsIntegrationOutbox");
        builder.HasKey(message => message.Id);
        builder.Property(message => message.EventType).HasMaxLength(150).IsRequired();
        builder.Property(message => message.AggregateType).HasMaxLength(100).IsRequired();
        builder.Property(message => message.AggregateKey).HasMaxLength(250).IsRequired();
        builder.Property(message => message.CorrelationId).HasMaxLength(100).IsRequired();
        builder.Property(message => message.CausationId).HasMaxLength(200);
        builder.Property(message => message.PayloadJson).HasMaxLength(1_000_000).IsRequired();
        builder.Property(message => message.PayloadHash).HasMaxLength(64).IsRequired();
        builder.Property(message => message.Status).HasMaxLength(30).IsRequired();
        builder.Property(message => message.LastError).HasMaxLength(2_000);
        builder.Property(message => message.OccurredAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(message => message.CreatedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(message => message.NextAttemptAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(message => message.LeaseUntilUtc).HasColumnType("timestamp with time zone");
        builder.Property(message => message.DeliveredAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(message => message.DeadLetteredAtUtc).HasColumnType("timestamp with time zone");
        builder.HasIndex(message => message.EventId).IsUnique();
        builder.HasIndex(message => new { message.Status, message.NextAttemptAtUtc, message.LeaseUntilUtc });
        builder.HasIndex(message => new { message.EventType, message.OccurredAtUtc });
    }
}

public sealed class WmsIntegrationInboxConfiguration : IEntityTypeConfiguration<WmsIntegrationInboxEntity>
{
    public void Configure(EntityTypeBuilder<WmsIntegrationInboxEntity> builder)
    {
        builder.ToTable("WmsIntegrationInbox");
        builder.HasKey(message => message.Id);
        builder.Property(message => message.SourceSystem).HasMaxLength(100).IsRequired();
        builder.Property(message => message.ExternalMessageId).HasMaxLength(250).IsRequired();
        builder.Property(message => message.EventType).HasMaxLength(150).IsRequired();
        builder.Property(message => message.PayloadHash).HasMaxLength(64).IsRequired();
        builder.Property(message => message.PayloadJson).HasMaxLength(1_000_000).IsRequired();
        builder.Property(message => message.CorrelationId).HasMaxLength(100);
        builder.Property(message => message.Status).HasMaxLength(30).IsRequired();
        builder.Property(message => message.LastError).HasMaxLength(2_000);
        builder.Property(message => message.ReceivedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(message => message.ProcessedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(message => message.LeaseUntilUtc).HasColumnType("timestamp with time zone");
        builder.HasIndex(message => new { message.SourceSystem, message.ExternalMessageId }).IsUnique();
        builder.HasIndex(message => new { message.Status, message.LeaseUntilUtc });
    }
}

public sealed class WmsWebhookSubscriptionConfiguration : IEntityTypeConfiguration<WmsWebhookSubscriptionEntity>
{
    public void Configure(EntityTypeBuilder<WmsWebhookSubscriptionEntity> builder)
    {
        builder.ToTable("WmsWebhookSubscriptions");
        builder.HasKey(subscription => subscription.Id);
        builder.Property(subscription => subscription.Name).HasMaxLength(200).IsRequired();
        builder.Property(subscription => subscription.EndpointUrl).HasMaxLength(2_000).IsRequired();
        builder.Property(subscription => subscription.EventTypesJson).HasMaxLength(4_000).IsRequired();
        builder.Property(subscription => subscription.WarehouseIdsJson).HasMaxLength(4_000).IsRequired();
        builder.Property(subscription => subscription.SecretCiphertext).HasMaxLength(4_000).IsRequired();
        builder.Property(subscription => subscription.PreviousSecretCiphertext).HasMaxLength(4_000);
        builder.Property(subscription => subscription.Status).HasMaxLength(30).IsRequired();
        builder.Property(subscription => subscription.CreatedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(subscription => subscription.UpdatedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(subscription => subscription.LastDeliveryAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(subscription => subscription.DisabledAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(subscription => subscription.PreviousSecretValidUntilUtc).HasColumnType("timestamp with time zone");
        builder.HasIndex(subscription => new { subscription.Status, subscription.UpdatedAtUtc });
    }
}

public sealed class WmsWebhookDeliveryConfiguration : IEntityTypeConfiguration<WmsWebhookDeliveryEntity>
{
    public void Configure(EntityTypeBuilder<WmsWebhookDeliveryEntity> builder)
    {
        builder.ToTable("WmsWebhookDeliveries");
        builder.HasKey(delivery => delivery.Id);
        builder.Property(delivery => delivery.Status).HasMaxLength(30).IsRequired();
        builder.Property(delivery => delivery.ResponseBody).HasMaxLength(8_000);
        builder.Property(delivery => delivery.LastError).HasMaxLength(2_000);
        builder.Property(delivery => delivery.NextAttemptAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(delivery => delivery.LeaseUntilUtc).HasColumnType("timestamp with time zone");
        builder.Property(delivery => delivery.DeliveredAtUtc).HasColumnType("timestamp with time zone");
        builder.HasOne(delivery => delivery.OutboxMessage)
            .WithMany(message => message.Deliveries)
            .HasForeignKey(delivery => delivery.OutboxMessageId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(delivery => delivery.Subscription)
            .WithMany(subscription => subscription.Deliveries)
            .HasForeignKey(delivery => delivery.SubscriptionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(delivery => new { delivery.OutboxMessageId, delivery.SubscriptionId }).IsUnique();
        builder.HasIndex(delivery => new { delivery.Status, delivery.NextAttemptAtUtc, delivery.LeaseUntilUtc });
    }
}
