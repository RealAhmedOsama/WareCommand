using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
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
    IConfiguration configuration,
    ILogger<SmtpAccountNotificationSender> logger) : IAccountNotificationSender
{
    public async Task SendPasswordResetAsync(
        WmsUser user,
        string resetUrl,
        CancellationToken cancellationToken = default)
    {
        var host = configuration["Authentication:Smtp:Host"];
        var fromAddress = configuration["Authentication:Smtp:FromAddress"];
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(fromAddress))
        {
            throw new InvalidOperationException(
                "Password reset email delivery is not configured. Set Authentication:Smtp:Host and Authentication:Smtp:FromAddress.");
        }

        if (string.IsNullOrWhiteSpace(user.Email))
        {
            throw new InvalidOperationException("The account does not have an email address for password reset delivery.");
        }

        var port = configuration.GetValue("Authentication:Smtp:Port", 587);
        var enableSsl = configuration.GetValue("Authentication:Smtp:EnableSsl", true);
        using var client = new SmtpClient(host, port)
        {
            EnableSsl = enableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false
        };

        var username = configuration["Authentication:Smtp:Username"];
        var password = configuration["Authentication:Smtp:Password"];
        if (!string.IsNullOrWhiteSpace(username))
        {
            client.Credentials = new NetworkCredential(username, password);
        }

        using var message = new MailMessage(fromAddress, user.Email)
        {
            Subject = "WareCommand password reset",
            Body =
                $"A password reset was requested for your WareCommand account. Follow this link to choose a new password:\n\n{resetUrl}\n\nIf you did not request this, you can ignore this message.",
            IsBodyHtml = false
        };

        await client.SendMailAsync(message, cancellationToken);
        logger.LogInformation("Password reset notification sent for user {UserId}", user.Id);
    }
}
