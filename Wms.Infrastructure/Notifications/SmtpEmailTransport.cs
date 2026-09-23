using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Wms.Application.Notifications;

namespace Wms.Infrastructure.Notifications;

public sealed class SmtpEmailTransport(
    IConfiguration configuration,
    IHostEnvironment environment) : IEmailTransport
{
    private const string SectionName = "Authentication:Smtp";
    private int _verified;

    public EmailTransportCapability Capability
    {
        get
        {
            var settings = ReadSettings();
            return new EmailTransportCapability(
                settings.Enabled,
                settings.Configured,
                settings.Configured && Volatile.Read(ref _verified) == 1);
        }
    }

    public async Task<EmailTransportResult> SendAsync(
        EmailMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var settings = ReadSettings();
        if (!settings.Enabled || !settings.Configured)
        {
            return new EmailTransportResult(false, false, "smtp_disabled_or_invalid");
        }

        if (!TryMailbox(message.Recipient, out var recipient) ||
            string.IsNullOrWhiteSpace(message.Subject) ||
            message.Subject.Length > 200 ||
            message.Subject.Any(char.IsControl) ||
            message.Body.Length > 100_000 ||
            !IsSafeMessageId(message.StableMessageId))
        {
            return new EmailTransportResult(false, false, "email_message_invalid");
        }

        using var client = new SmtpClient(settings.Host, settings.Port)
        {
            EnableSsl = settings.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            // The linked cancellation token owns timeout classification. Keep SmtpClient's
            // native fallback later so the two deadlines cannot race into different errors.
            Timeout = checked((settings.TimeoutSeconds + 5) * 1_000)
        };
        if (!string.IsNullOrEmpty(settings.Username))
        {
            client.Credentials = new NetworkCredential(settings.Username, settings.Password);
        }

        using var mail = new MailMessage(settings.FromAddress, recipient)
        {
            Subject = message.Subject,
            SubjectEncoding = Encoding.UTF8,
            Body = NormalizeLineEndings(message.Body),
            BodyEncoding = Encoding.UTF8,
            HeadersEncoding = Encoding.UTF8,
            IsBodyHtml = false
        };
        mail.Headers.Add("Message-ID", $"<{message.StableMessageId}@notifications.warecommand.local>");
        mail.Headers.Add("X-WareCommand-Delivery-ID", message.StableMessageId);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
        try
        {
            await client.SendMailAsync(mail, timeout.Token);
            Volatile.Write(ref _verified, 1);
            return new EmailTransportResult(true, false, "smtp_accepted");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new EmailTransportResult(false, true, "smtp_timeout");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (Exception) when (timeout.IsCancellationRequested)
        {
            return new EmailTransportResult(false, true, "smtp_timeout");
        }
        catch (SmtpException exception)
        {
            var statusCode = (int)exception.StatusCode;
            return new EmailTransportResult(
                false,
                statusCode < 400 || statusCode is >= 400 and < 500,
                statusCode is >= 500 and < 600 ? "smtp_rejected_permanently" : "smtp_transport_failed");
        }
        catch (Exception exception) when (
            exception is SocketException or IOException or TimeoutException)
        {
            return new EmailTransportResult(false, true, "smtp_transport_failed");
        }
        catch (FormatException)
        {
            return new EmailTransportResult(false, false, "email_message_invalid");
        }
    }

    private SmtpSettings ReadSettings()
    {
        var section = configuration.GetSection(SectionName);
        var host = section["Host"]?.Trim();
        var fromAddress = section["FromAddress"]?.Trim();
        var enabledValue = section["Enabled"];
        var inferredEnabled = !string.IsNullOrWhiteSpace(host) && !string.IsNullOrWhiteSpace(fromAddress);
        if (enabledValue is not null && !bool.TryParse(enabledValue, out _))
        {
            return SmtpSettings.Disabled;
        }

        var enabled = enabledValue is null
            ? inferredEnabled
            : bool.Parse(enabledValue);
        if (!enabled)
        {
            return SmtpSettings.Disabled;
        }

        if (string.IsNullOrWhiteSpace(host) ||
            host.Length > 253 ||
            host.Any(char.IsControl) ||
            Uri.CheckHostName(host) == UriHostNameType.Unknown ||
            !TryMailbox(fromAddress, out var normalizedFrom) ||
            !TryInteger(section["Port"], 587, 1, 65_535, out var port) ||
            !TryInteger(section["TimeoutSeconds"], 20, 1, 120, out var timeoutSeconds) ||
            !TryBoolean(section["EnableSsl"], true, out var enableSsl) ||
            !TryBoolean(section["AllowInsecureLocalhost"], false, out var allowInsecureLocalhost))
        {
            return SmtpSettings.Disabled;
        }

        var username = section["Username"];
        var password = section["Password"];
        if (string.IsNullOrWhiteSpace(username) != string.IsNullOrWhiteSpace(password) ||
            username?.Any(char.IsControl) == true ||
            password?.Any(char.IsControl) == true)
        {
            return SmtpSettings.Disabled;
        }

        if (!enableSsl &&
            !(environment.IsDevelopment() &&
              allowInsecureLocalhost &&
              IPAddress.TryParse(host, out var address) &&
              IPAddress.IsLoopback(address)))
        {
            return SmtpSettings.Disabled;
        }

        return new SmtpSettings(
            Enabled: true,
            Configured: true,
            host,
            port,
            enableSsl,
            allowInsecureLocalhost,
            timeoutSeconds,
            normalizedFrom,
            username,
            password);
    }

    private static bool TryMailbox(string? value, out string address)
    {
        address = string.Empty;
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl))
        {
            return false;
        }

        try
        {
            address = new MailAddress(value.Trim()).Address;
            return address.Length <= 320;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsSafeMessageId(string value) =>
        value.Length is > 0 and <= 100 &&
        value.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');

    private static string NormalizeLineEndings(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace("\n", "\r\n", StringComparison.Ordinal);

    private static bool TryInteger(string? value, int defaultValue, int minimum, int maximum, out int result)
    {
        result = defaultValue;
        if (value is null)
        {
            return true;
        }

        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result) &&
            result >= minimum && result <= maximum;
    }

    private static bool TryBoolean(string? value, bool defaultValue, out bool result)
    {
        result = defaultValue;
        return value is null || bool.TryParse(value, out result);
    }

    private sealed record SmtpSettings(
        bool Enabled,
        bool Configured,
        string Host,
        int Port,
        bool EnableSsl,
        bool AllowInsecureLocalhost,
        int TimeoutSeconds,
        string FromAddress,
        string? Username,
        string? Password)
    {
        public static SmtpSettings Disabled { get; } = new(
            Enabled: false,
            Configured: false,
            string.Empty,
            587,
            true,
            false,
            20,
            string.Empty,
            null,
            null);
    }
}
