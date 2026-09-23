using Microsoft.Extensions.Logging;
using Wms.Application.Notifications;
using Wms.Infrastructure.Identity;

namespace Wms.ASP.Identity;

public interface IAccountNotificationSender
{
    Task SendPasswordResetAsync(
        WmsUser user,
        string resetUrl,
        CancellationToken cancellationToken = default);
}

public sealed class SmtpAccountNotificationSender(
    IEmailTransport emailTransport,
    ILogger<SmtpAccountNotificationSender> logger) : IAccountNotificationSender
{
    public async Task SendPasswordResetAsync(
        WmsUser user,
        string resetUrl,
        CancellationToken cancellationToken = default)
    {
        if (!emailTransport.Capability.Enabled || !emailTransport.Capability.Configured)
        {
            throw new InvalidOperationException(
                "Password reset email delivery is not configured. Set Authentication:Smtp:Host and Authentication:Smtp:FromAddress.");
        }

        if (string.IsNullOrWhiteSpace(user.Email))
        {
            throw new InvalidOperationException("The account does not have an email address for password reset delivery.");
        }

        var result = await emailTransport.SendAsync(
            new EmailMessage(
                user.Email,
                "WareCommand password reset",
                $"A password reset was requested for your WareCommand account. Follow this link to choose a new password:\n\n{resetUrl}\n\nIf you did not request this, you can ignore this message.",
                $"password-reset-{Guid.NewGuid():N}"),
            cancellationToken);
        if (!result.Accepted)
        {
            throw new InvalidOperationException("Password reset email delivery failed.");
        }

        logger.LogInformation("Password reset message accepted by the configured SMTP server.");
    }
}
