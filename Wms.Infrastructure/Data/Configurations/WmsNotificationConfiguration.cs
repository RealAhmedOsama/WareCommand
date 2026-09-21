using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Infrastructure.Notifications;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class WmsNotificationConfiguration : IEntityTypeConfiguration<WmsNotificationEntity>
{
    public void Configure(EntityTypeBuilder<WmsNotificationEntity> builder)
    {
        builder.ToTable("WmsNotifications");
        builder.HasKey(notification => notification.Id);
        builder.Property(notification => notification.DeduplicationKey).HasMaxLength(250).IsRequired();
        builder.Property(notification => notification.CooldownKey).HasMaxLength(250);
        builder.Property(notification => notification.Kind).HasMaxLength(100).IsRequired();
        builder.Property(notification => notification.Severity).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(notification => notification.TitleEn).HasMaxLength(200).IsRequired();
        builder.Property(notification => notification.TitleAr).HasMaxLength(200).IsRequired();
        builder.Property(notification => notification.MessageEn).HasMaxLength(2_000).IsRequired();
        builder.Property(notification => notification.MessageAr).HasMaxLength(2_000).IsRequired();
        builder.Property(notification => notification.SourceType).HasMaxLength(100);
        builder.Property(notification => notification.SourceId).HasMaxLength(200);
        builder.Property(notification => notification.SourceReference).HasMaxLength(250);
        builder.Property(notification => notification.RequiredPermission).HasMaxLength(100);
        builder.Property(notification => notification.DeepLink).HasMaxLength(500);
        builder.Property(notification => notification.CorrelationId).HasMaxLength(100).IsRequired();
        builder.Property(notification => notification.CreatedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(notification => notification.ExpiresAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(notification => notification.ResolvedAtUtc).HasColumnType("timestamp with time zone");
        builder.HasIndex(notification => notification.DeduplicationKey).IsUnique();
        builder.HasIndex(notification => new { notification.CooldownKey, notification.CreatedAtUtc });
        builder.HasIndex(notification => new { notification.Kind, notification.CreatedAtUtc });
    }
}

public sealed class WmsNotificationRecipientConfiguration : IEntityTypeConfiguration<WmsNotificationRecipientEntity>
{
    public void Configure(EntityTypeBuilder<WmsNotificationRecipientEntity> builder)
    {
        builder.ToTable("WmsNotificationRecipients");
        builder.HasKey(recipient => recipient.Id);
        builder.Property(recipient => recipient.RecipientUserId).HasMaxLength(450).IsRequired();
        builder.Property(recipient => recipient.RecipientEmail).HasMaxLength(320);
        builder.Property(recipient => recipient.Channel).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(recipient => recipient.DeliveryStatus).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(recipient => recipient.LastError).HasMaxLength(2_000);
        builder.Property(recipient => recipient.CreatedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(recipient => recipient.LastAttemptAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(recipient => recipient.NextAttemptAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(recipient => recipient.LeaseUntilUtc).HasColumnType("timestamp with time zone");
        builder.Property(recipient => recipient.ReadAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(recipient => recipient.AcknowledgedAtUtc).HasColumnType("timestamp with time zone");
        builder.HasOne(recipient => recipient.Notification)
            .WithMany(notification => notification.Recipients)
            .HasForeignKey(recipient => recipient.NotificationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(recipient => new { recipient.NotificationId, recipient.RecipientUserId, recipient.Channel })
            .IsUnique();
        builder.HasIndex(recipient => new
        {
            recipient.RecipientUserId,
            recipient.ReadAtUtc,
            recipient.CreatedAtUtc
        });
        builder.HasIndex(recipient => new
        {
            recipient.DeliveryStatus,
            recipient.NextAttemptAtUtc,
            recipient.LeaseUntilUtc
        });
    }
}

public sealed class WmsNotificationPreferenceConfiguration : IEntityTypeConfiguration<WmsNotificationPreferenceEntity>
{
    public void Configure(EntityTypeBuilder<WmsNotificationPreferenceEntity> builder)
    {
        builder.ToTable("WmsNotificationPreferences");
        builder.HasKey(preference => preference.Id);
        builder.Property(preference => preference.Scope).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(preference => preference.UserId).HasMaxLength(450);
        builder.Property(preference => preference.RoleName).HasMaxLength(100);
        builder.Property(preference => preference.Kind).HasMaxLength(100);
        builder.Property(preference => preference.Channel).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(preference => preference.TimeZone).HasMaxLength(100).IsRequired();
        builder.Property(preference => preference.UpdatedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(preference => new
        {
            preference.Scope,
            preference.UserId,
            preference.RoleName,
            preference.WarehouseId,
            preference.Kind,
            preference.Channel
        }).IsUnique();
        builder.HasIndex(preference => new { preference.UserId, preference.UpdatedAtUtc });
    }
}
