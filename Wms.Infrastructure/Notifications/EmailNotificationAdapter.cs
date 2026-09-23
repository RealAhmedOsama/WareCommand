using System.Globalization;
using Wms.Application.Notifications;
using Wms.Domain.Enums;

namespace Wms.Infrastructure.Notifications;

public sealed class EmailNotificationAdapter(IEmailTransport transport) : INotificationChannelAdapter
{
    public NotificationChannel Channel => NotificationChannel.Email;

    public async Task<NotificationDeliveryResult> DeliverAsync(
        NotificationDeliveryMessage message,
        CancellationToken cancellationToken = default)
    {
        if (!transport.Capability.Enabled || !transport.Capability.Configured)
        {
            return new NotificationDeliveryResult(
                Succeeded: false,
                Retryable: false,
                Error: "smtp_disabled_or_invalid",
                Disabled: true);
        }

        if (string.IsNullOrWhiteSpace(message.RecipientEmail))
        {
            return new NotificationDeliveryResult(
                Succeeded: false,
                Retryable: false,
                Error: "recipient_email_unavailable",
                Disabled: true);
        }

        var isArabic = message.Locale.StartsWith("ar", StringComparison.OrdinalIgnoreCase);
        var title = isArabic ? message.TitleAr : message.TitleEn;
        var bodyText = isArabic ? message.MessageAr : message.MessageEn;
        var heading = isArabic ? "إشعار WareCommand" : "WareCommand notification";
        var severity = isArabic
            ? message.Severity switch
            {
                NotificationSeverity.Critical => "حرج",
                NotificationSeverity.Warning => "تحذير",
                _ => "معلومات"
            }
            : message.Severity.ToString();
        var text = new List<string>
        {
            heading,
            string.Empty,
            isArabic ? $"الخطورة: {severity}" : $"Severity: {severity}",
            string.Empty,
            title,
            bodyText
        };
        if (IsSafeLocalLink(message.DeepLink))
        {
            text.Add(string.Empty);
            text.Add(isArabic ? $"افتح WareCommand: {message.DeepLink}" : $"Open WareCommand: {message.DeepLink}");
        }

        var email = await transport.SendAsync(
            new EmailMessage(
                message.RecipientEmail,
                isArabic ? "إشعار WareCommand" : "WareCommand notification",
                string.Join(Environment.NewLine, text),
                $"notification-{message.RecipientId.ToString(CultureInfo.InvariantCulture)}"),
            cancellationToken);
        return email.Accepted
            ? new NotificationDeliveryResult(
                Succeeded: true,
                Retryable: false,
                SuccessStatus: NotificationDeliveryStatus.TransportAccepted)
            : new NotificationDeliveryResult(
                Succeeded: false,
                Retryable: email.Retryable,
                Error: email.ErrorCode);
    }

    private static bool IsSafeLocalLink(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.StartsWith('/') &&
        !value.StartsWith("//", StringComparison.Ordinal) &&
        value.Length <= 500 &&
        !value.Any(char.IsControl);
}
