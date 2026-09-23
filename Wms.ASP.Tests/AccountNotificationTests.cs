using Microsoft.Extensions.Logging.Abstractions;
using Wms.Application.Notifications;
using Wms.ASP.Identity;
using Wms.Infrastructure.Identity;
using Xunit;

namespace Wms.ASP.Tests;

public sealed class AccountNotificationTests
{
    [Fact]
    public async Task Password_reset_uses_the_shared_transport_and_preserves_message_content()
    {
        var transport = new CapturingEmailTransport();
        var sender = new SmtpAccountNotificationSender(
            transport,
            NullLogger<SmtpAccountNotificationSender>.Instance);
        var user = new WmsUser
        {
            Id = "private-user-id",
            Email = "account@example.test"
        };
        const string resetUrl = "https://warecommand.example.test/reset?token=private-reset-token";

        await sender.SendPasswordResetAsync(user, resetUrl);

        Assert.Equal("account@example.test", transport.Message?.Recipient);
        Assert.Equal("WareCommand password reset", transport.Message?.Subject);
        Assert.Contains(resetUrl, transport.Message?.Body, StringComparison.Ordinal);
        Assert.StartsWith("password-reset-", transport.Message?.StableMessageId, StringComparison.Ordinal);
    }

    private sealed class CapturingEmailTransport : IEmailTransport
    {
        public EmailTransportCapability Capability { get; } = new(true, true, false);
        public EmailMessage? Message { get; private set; }

        public Task<EmailTransportResult> SendAsync(
            EmailMessage message,
            CancellationToken cancellationToken = default)
        {
            Message = message;
            return Task.FromResult(new EmailTransportResult(true, false, "smtp_accepted"));
        }
    }
}
