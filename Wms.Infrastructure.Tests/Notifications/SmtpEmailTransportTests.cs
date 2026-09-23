using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Wms.Application.Notifications;
using Wms.Domain.Enums;
using Wms.Infrastructure.Notifications;

namespace Wms.Infrastructure.Tests.Notifications;

public sealed class SmtpEmailTransportTests
{
    [Fact]
    public async Task Development_local_sink_receives_utf8_mail_and_stable_safe_headers()
    {
        await using var sink = await LocalSmtpSink.StartAsync();
        var transport = CreateTransport(sink.Port, Environments.Development, enableSsl: false);
        var adapter = new EmailNotificationAdapter(transport);

        var result = await adapter.DeliverAsync(new NotificationDeliveryMessage(
            NotificationId: 42,
            RecipientId: 123,
            RecipientUserId: "private-user",
            RecipientEmail: "warehouse.user@example.test",
            Kind: "stock.low",
            Severity: NotificationSeverity.Warning,
            TitleEn: "Low stock",
            TitleAr: "مخزون منخفض",
            MessageEn: "Stock is below the threshold.",
            MessageAr: "تم استلام ١٢ وحدة.",
            DeepLink: "/inventory/1",
            WarehouseId: 17,
            Mandatory: false,
            CorrelationId: "notification-test",
            Locale: "ar-SA"));

        result.Succeeded.Should().BeTrue();
        result.SuccessStatus.Should().Be(NotificationDeliveryStatus.TransportAccepted);
        transport.Capability.Verified.Should().BeTrue();
        sink.Recipient.Should().Be("warehouse.user@example.test");
        sink.Message.Should().Contain("Message-ID: <notification-123@notifications.warecommand.local>");
        sink.Message.Should().Contain("X-WareCommand-Delivery-ID: notification-123");
        sink.Message.Should().Contain("charset=utf-8");
        sink.Message.Should().Contain("Subject: =?utf-8?B?");
        DecodeSubject(sink.Message).Should().Be("إشعار WareCommand");
        DecodeTextBody(sink.Message).Should().Be(
            "إشعار WareCommand\r\n\r\nالخطورة: تحذير\r\n\r\nمخزون منخفض\r\nتم استلام ١٢ وحدة.\r\n\r\nافتح WareCommand: /inventory/1");
    }

    [Fact]
    public async Task Email_adapter_selects_the_english_template_for_an_english_recipient()
    {
        var transport = new CapturingEmailTransport();
        var adapter = new EmailNotificationAdapter(transport);

        var result = await adapter.DeliverAsync(new NotificationDeliveryMessage(
            NotificationId: 43,
            RecipientId: 124,
            RecipientUserId: "private-user",
            RecipientEmail: "warehouse.user@example.test",
            Kind: "stock.low",
            Severity: NotificationSeverity.Warning,
            TitleEn: "Low stock",
            TitleAr: "مخزون منخفض",
            MessageEn: "Stock is below the threshold.",
            MessageAr: "المخزون أقل من الحد.",
            DeepLink: null,
            WarehouseId: 17,
            Mandatory: false,
            CorrelationId: "notification-test",
            Locale: "en-US"));

        result.SuccessStatus.Should().Be(NotificationDeliveryStatus.TransportAccepted);
        transport.Message.Should().NotBeNull();
        transport.Message!.Subject.Should().Be("WareCommand notification");
        transport.Message.Body.Should().Contain("Severity: Warning");
        transport.Message.Body.Should().Contain("Low stock");
        transport.Message.Body.Should().Contain("Stock is below the threshold.");
    }

    [Theory]
    [InlineData(450, true)]
    [InlineData(550, false)]
    public async Task Smtp_recipient_response_controls_bounded_retry_classification(
        int responseCode,
        bool retryable)
    {
        await using var sink = await LocalSmtpSink.StartAsync(responseCode);
        var transport = CreateTransport(sink.Port, Environments.Development, enableSsl: false);

        var result = await transport.SendAsync(new EmailMessage(
            "warehouse.user@example.test",
            "WareCommand notification",
            "Stock is low.",
            "notification-456"));

        result.Accepted.Should().BeFalse();
        result.Retryable.Should().Be(retryable);
        result.ErrorCode.Should().Be(retryable ? "smtp_transport_failed" : "smtp_rejected_permanently");
    }

    [Fact]
    public async Task Plain_smtp_is_rejected_outside_development_and_invalid_config_never_sends()
    {
        var transport = CreateTransport(25_252, Environments.Production, enableSsl: false);

        transport.Capability.Configured.Should().BeFalse();
        var result = await transport.SendAsync(new EmailMessage(
            "warehouse.user@example.test",
            "WareCommand notification",
            "Should not connect.",
            "notification-789"));

        result.Accepted.Should().BeFalse();
        result.Retryable.Should().BeFalse();
        result.ErrorCode.Should().Be("smtp_disabled_or_invalid");
    }

    [Fact]
    public async Task Smtp_timeout_is_retryable_and_caller_cancellation_is_propagated()
    {
        await using (var slowSink = await LocalSmtpSink.StartAsync(recipientDelayMilliseconds: 3_000))
        {
            var transport = CreateTransport(
                slowSink.Port,
                Environments.Development,
                enableSsl: false,
                timeoutSeconds: 1);
            var timedOut = await transport.SendAsync(new EmailMessage(
                "warehouse.user@example.test",
                "WareCommand notification",
                "Stock is low.",
                "notification-timeout"));

            timedOut.Accepted.Should().BeFalse();
            timedOut.Retryable.Should().BeTrue();
            timedOut.ErrorCode.Should().Be("smtp_timeout");
        }

        await using var canceledSink = await LocalSmtpSink.StartAsync(recipientDelayMilliseconds: 3_000);
        var cancellableTransport = CreateTransport(
            canceledSink.Port,
            Environments.Development,
            enableSsl: false,
            timeoutSeconds: 5);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var send = () => cancellableTransport.SendAsync(
            new EmailMessage(
                "warehouse.user@example.test",
                "WareCommand notification",
                "Stock is low.",
                "notification-canceled"),
            cancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(send);
    }

    private static SmtpEmailTransport CreateTransport(
        int port,
        string environmentName,
        bool enableSsl,
        int timeoutSeconds = 5)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Smtp:Enabled"] = "true",
                ["Authentication:Smtp:Host"] = "127.0.0.1",
                ["Authentication:Smtp:Port"] = port.ToString(CultureInfo.InvariantCulture),
                ["Authentication:Smtp:EnableSsl"] = enableSsl.ToString(),
                ["Authentication:Smtp:AllowInsecureLocalhost"] = "true",
                ["Authentication:Smtp:TimeoutSeconds"] = timeoutSeconds.ToString(CultureInfo.InvariantCulture),
                ["Authentication:Smtp:FromAddress"] = "notifications@example.test"
            })
            .Build();
        return new SmtpEmailTransport(configuration, new TestHostEnvironment(environmentName));
    }

    private static string DecodeTextBody(string message)
    {
        var separator = message.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        separator.Should().BeGreaterThanOrEqualTo(0);
        var encoded = message[(separator + 4)..]
            .Replace("\r\n", string.Empty, StringComparison.Ordinal)
            .TrimEnd('.');
        return Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
    }

    private static string DecodeSubject(string message)
    {
        const string encodedWord = "=?utf-8?B?";
        var subject = message.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
            .Single(line => line.StartsWith("Subject: ", StringComparison.Ordinal));
        var start = subject.IndexOf(encodedWord, StringComparison.OrdinalIgnoreCase) + encodedWord.Length;
        var end = subject.IndexOf("?=", start, StringComparison.Ordinal);
        return Encoding.UTF8.GetString(Convert.FromBase64String(subject[start..end]));
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Wms.Infrastructure.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
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

    private sealed class LocalSmtpSink : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cancellation = new();
        private readonly Task _server;

        private LocalSmtpSink(TcpListener listener, int recipientResponseCode, int recipientDelayMilliseconds)
        {
            _listener = listener;
            RecipientResponseCode = recipientResponseCode;
            RecipientDelayMilliseconds = recipientDelayMilliseconds;
            _server = RunAsync(_cancellation.Token);
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
        public int RecipientResponseCode { get; }
        public int RecipientDelayMilliseconds { get; }
        public string? Recipient { get; private set; }
        public string? Message { get; private set; }

        public static Task<LocalSmtpSink> StartAsync(
            int recipientResponseCode = 250,
            int recipientDelayMilliseconds = 0)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return Task.FromResult(new LocalSmtpSink(listener, recipientResponseCode, recipientDelayMilliseconds));
        }

        public async ValueTask DisposeAsync()
        {
            _cancellation.Cancel();
            _listener.Stop();
            try
            {
                await _server;
            }
            catch (OperationCanceledException)
            {
            }
            catch (SocketException)
            {
            }
            catch (IOException)
            {
            }

            _cancellation.Dispose();
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            using var client = await _listener.AcceptTcpClientAsync(cancellationToken);
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            await using var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true)
            {
                NewLine = "\r\n",
                AutoFlush = true
            };
            await writer.WriteLineAsync("220 localhost ESMTP ready");

            while (!cancellationToken.IsCancellationRequested)
            {
                var command = await reader.ReadLineAsync(cancellationToken);
                if (command is null)
                {
                    return;
                }

                if (command.StartsWith("EHLO ", StringComparison.OrdinalIgnoreCase) ||
                    command.StartsWith("HELO ", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("250-localhost");
                    await writer.WriteLineAsync("250-SIZE 1000000");
                    await writer.WriteLineAsync("250 8BITMIME");
                }
                else if (command.StartsWith("MAIL FROM:", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("250 sender accepted");
                }
                else if (command.StartsWith("RCPT TO:", StringComparison.OrdinalIgnoreCase))
                {
                    var start = command.IndexOf('<');
                    var end = command.IndexOf('>');
                    Recipient = start >= 0 && end > start ? command[(start + 1)..end] : null;
                    if (RecipientDelayMilliseconds > 0)
                    {
                        await Task.Delay(RecipientDelayMilliseconds, cancellationToken);
                    }

                    await writer.WriteLineAsync($"{RecipientResponseCode} recipient response");
                }
                else if (command.Equals("DATA", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("354 end with dot");
                    var lines = new List<string>();
                    while (true)
                    {
                        var line = await reader.ReadLineAsync(cancellationToken);
                        if (line is null || line == ".")
                        {
                            break;
                        }

                        lines.Add(line.StartsWith("..", StringComparison.Ordinal) ? line[1..] : line);
                    }

                    Message = string.Join("\r\n", lines);
                    await writer.WriteLineAsync("250 message accepted");
                }
                else if (command.Equals("QUIT", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("221 closing connection");
                    return;
                }
                else
                {
                    await writer.WriteLineAsync("250 command accepted");
                }
            }
        }
    }
}
