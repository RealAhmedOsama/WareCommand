using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Wms.Application.Integrations;
using Wms.Application.Telemetry;
using Wms.Infrastructure.Integrations;

namespace Wms.Infrastructure.Tests.Integrations;

public sealed class WebhookHttpDeliveryTransportTests
{
    private const string Secret = "whsec-test-secret";
    [Fact]
    public async Task SendsExactUtf8PayloadAndStableSignedDeliveryIdentity()
    {
        byte[]? receivedBody = null;
        Dictionary<string, string>? receivedHeaders = null;
        await using var receiver = await Receiver.StartAsync(async context =>
        {
            await using var body = new MemoryStream();
            await context.Request.Body.CopyToAsync(body, context.RequestAborted);
            receivedBody = body.ToArray();
            receivedHeaders = context.Request.Headers.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.ToString(),
                StringComparer.OrdinalIgnoreCase);
            context.Response.StatusCode = StatusCodes.Status202Accepted;
            await context.Response.WriteAsync("accepted", context.RequestAborted);
        });
        using var client = CreateTransport(receiver.BaseAddress, out var transport);
        const string payload = "{\"name\":\"Café\",\"quantity\":1.25}";
        var eventId = Guid.NewGuid();
        const long deliveryId = 912;
        var timestamp = DateTimeOffset.UtcNow;
        var request = CreateRequest(
            receiver.BaseAddress,
            payload,
            eventId,
            deliveryId,
            WebhookSignature.CreateHeaders(
                Secret,
                payload,
                timestamp,
                eventId,
                deliveryId,
                "inventory.movement-recorded",
                3));

        var result = await transport.SendAsync(request);

        Assert.Equal(new WebhookDeliveryTransportCapability(true, true), transport.Capability);
        Assert.True(result.Succeeded);
        Assert.False(result.Retryable);
        Assert.Equal(202, result.ResponseStatusCode);
        Assert.Equal(Encoding.UTF8.GetBytes(payload), receivedBody);
        Assert.NotNull(receivedHeaders);
        Assert.Equal(eventId.ToString("D"), receivedHeaders[WebhookSignature.EventIdHeaderName]);
        Assert.Equal(deliveryId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            receivedHeaders[WebhookSignature.DeliveryIdHeaderName]);
        Assert.Equal("inventory.movement-recorded", receivedHeaders[WebhookSignature.EventTypeHeaderName]);
        Assert.Equal("3", receivedHeaders[WebhookSignature.VersionHeaderName]);
        Assert.Equal("application/json", receivedHeaders["Content-Type"]);
        Assert.True(WebhookSignature.Verify(
            Secret,
            Encoding.UTF8.GetString(receivedBody!),
            receivedHeaders[WebhookSignature.HeaderName],
            timestamp));
    }

    [Theory]
    [InlineData(204, true, false)]
    [InlineData(429, false, true)]
    [InlineData(503, false, true)]
    [InlineData(422, false, false)]
    public async Task MapsHttpResponseClassesToDeliveryOutcomes(
        int statusCode,
        bool succeeded,
        bool retryable)
    {
        await using var receiver = await Receiver.StartAsync(async context =>
        {
            context.Response.StatusCode = statusCode;
            if (statusCode == 429)
            {
                context.Response.Headers.RetryAfter = "120";
            }

            await context.Response.WriteAsync(new string('x', 200));
        });
        using var client = CreateTransport(receiver.BaseAddress, out var transport, maximumResponseBytes: 48);

        var result = await transport.SendAsync(CreateRequest(receiver.BaseAddress));

        Assert.Equal(succeeded, result.Succeeded);
        Assert.Equal(retryable, result.Retryable);
        Assert.Equal(statusCode, result.ResponseStatusCode);
        Assert.True((result.ResponseBody?.Length ?? 0) <= 48);
        if (statusCode == 429)
        {
            Assert.NotNull(result.RetryAfterUtc);
        }
    }

    [Fact]
    public async Task RequestTimeoutIsRetryable()
    {
        await using var receiver = await Receiver.StartAsync(async context =>
        {
            await Task.Delay(TimeSpan.FromSeconds(1), context.RequestAborted);
        });
        using var client = CreateTransport(
            receiver.BaseAddress,
            out var transport,
            requestTimeout: TimeSpan.FromMilliseconds(50));

        var result = await transport.SendAsync(CreateRequest(receiver.BaseAddress));

        Assert.False(result.Succeeded);
        Assert.True(result.Retryable);
        Assert.Equal("webhook_request_timed_out", result.Error);
    }

    [Fact]
    public async Task RequestBodyLimitUsesUtf8ByteCount()
    {
        var options = new WebhookDeliveryTransportOptions
        {
            Enabled = true,
            AllowedHosts = ["webhook.example.test"],
            AllowedSchemes = [Uri.UriSchemeHttps],
            AllowedPorts = [443],
            MaximumRequestBodyBytes = 1
        };
        using var client = CreateTransport(
            options,
            out var transport,
            new FixedWebhookDnsResolver([IPAddress.Loopback]));

        var result = await transport.SendAsync(CreateRequest(
            new Uri("https://webhook.example.test/events"),
            "é"));

        Assert.False(result.Succeeded);
        Assert.False(result.Retryable);
        Assert.Equal("webhook_request_body_too_large", result.Error);
    }

    [Fact]
    public async Task DoesNotFollowRedirects()
    {
        var redirected = false;
        await using var receiver = await Receiver.StartAsync(async context =>
        {
            if (context.Request.Path == "/target")
            {
                redirected = true;
                context.Response.StatusCode = StatusCodes.Status200OK;
                return;
            }

            context.Response.StatusCode = StatusCodes.Status302Found;
            context.Response.Headers.Location = "/target";
            await Task.CompletedTask;
        });
        using var client = CreateTransport(receiver.BaseAddress, out var transport);

        var result = await transport.SendAsync(CreateRequest(receiver.BaseAddress));

        Assert.False(result.Succeeded);
        Assert.False(result.Retryable);
        Assert.Equal(302, result.ResponseStatusCode);
        Assert.False(redirected);
    }

    [Fact]
    public async Task RejectsDnsRebindingToLoopbackBeforeConnecting()
    {
        var options = new WebhookDeliveryTransportOptions
        {
            Enabled = true,
            AllowedHosts = ["webhook.example.test"],
            AllowedSchemes = [Uri.UriSchemeHttps],
            AllowedPorts = [443]
        };
        var resolver = new FixedWebhookDnsResolver([IPAddress.Loopback]);
        using var client = CreateTransport(options, out var transport, resolver);
        var request = CreateRequest(
            new Uri("https://webhook.example.test/events"));

        var result = await transport.SendAsync(request);

        Assert.False(result.Succeeded);
        Assert.True(result.Retryable);
        Assert.Equal("webhook_connection_failed", result.Error);
    }

    [Theory]
    [InlineData("10.0.0.1")]
    [InlineData("169.254.169.254")]
    [InlineData("168.63.129.16")]
    [InlineData("::1")]
    [InlineData("fc00::1")]
    [InlineData("fe80::1")]
    [InlineData("2001:db8::1")]
    [InlineData("2001:2::1")]
    [InlineData("64:ff9b::a00:1")]
    public async Task RejectsPrivateLinkLocalMetadataAndReservedDnsAnswers(string addressText)
    {
        var options = new WebhookDeliveryTransportOptions
        {
            Enabled = true,
            AllowedHosts = ["webhook.example.test"],
            AllowedSchemes = [Uri.UriSchemeHttps],
            AllowedPorts = [443]
        };
        var resolver = new FixedWebhookDnsResolver([IPAddress.Parse(addressText)]);
        using var client = CreateTransport(options, out var transport, resolver);

        var result = await transport.SendAsync(CreateRequest(
            new Uri("https://webhook.example.test/events")));

        Assert.False(result.Succeeded);
        Assert.True(result.Retryable);
        Assert.Equal("webhook_connection_failed", result.Error);
    }

    [Fact]
    public async Task TransportLogsDoNotContainSecretsOrPayloads()
    {
        var options = new WebhookDeliveryTransportOptions
        {
            Enabled = true,
            AllowedHosts = ["webhook.example.test"],
            AllowedSchemes = [Uri.UriSchemeHttps],
            AllowedPorts = [443]
        };
        var logger = new CapturingLogger<HttpWebhookDeliveryTransport>();
        using var client = CreateTransport(
            options,
            out var transport,
            new FixedWebhookDnsResolver([IPAddress.Loopback]),
            logger: logger);
        const string sensitivePayload = "private inventory payload marker";

        var result = await transport.SendAsync(CreateRequest(
            new Uri("https://webhook.example.test/events"),
            sensitivePayload));

        Assert.True(result.Retryable);
        var logged = string.Join(Environment.NewLine, logger.Entries);
        Assert.DoesNotContain(Secret, logged, StringComparison.Ordinal);
        Assert.DoesNotContain(sensitivePayload, logged, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WebhookActivityOmitsDestinationSecretsAndPayload()
    {
        var activities = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == WmsTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => activities.Add(activity)
        };
        ActivitySource.AddActivityListener(listener);
        var options = new WebhookDeliveryTransportOptions
        {
            Enabled = true,
            AllowedHosts = ["webhook.example.test"],
            AllowedSchemes = [Uri.UriSchemeHttps],
            AllowedPorts = [443]
        };
        using var client = CreateTransport(
            options,
            out var transport,
            new FixedWebhookDnsResolver([IPAddress.Loopback]));
        const string destinationSecret = "private-path-and-query-token";
        const string sensitivePayload = "private warehouse quantities";
        var request = CreateRequest(
            new Uri("https://webhook.example.test/events"),
            sensitivePayload) with
        {
            EndpointUrl = $"https://webhook.example.test/hooks/{destinationSecret}?token={destinationSecret}"
        };

        var result = await transport.SendAsync(request);

        Assert.True(result.Retryable);
        var activity = Assert.Single(activities, candidate =>
            Equals(candidate.GetTagItem("wms.webhook.event_id"), request.EventId.ToString("D")));
        var telemetry = string.Join(
            Environment.NewLine,
            activity.DisplayName,
            string.Join(";", activity.TagObjects.Select(tag => $"{tag.Key}={tag.Value}")));
        Assert.DoesNotContain(destinationSecret, telemetry, StringComparison.Ordinal);
        Assert.DoesNotContain(sensitivePayload, telemetry, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, telemetry, StringComparison.Ordinal);
        Assert.Contains("wms.webhook.delivery_id", telemetry, StringComparison.Ordinal);
        Assert.Contains("wms.webhook.event_type", telemetry, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsLoopbackHttpOutsideDevelopmentEvenWhenTheExceptionIsEnabled()
    {
        var options = new WebhookDeliveryTransportOptions
        {
            Enabled = true,
            AllowedHosts = ["127.0.0.1"],
            AllowedSchemes = [Uri.UriSchemeHttp],
            AllowedPorts = [5000],
            AllowLocalHttpForDevelopment = true
        };
        var validator = new WebhookDeliveryTransportOptionsValidator(
            new TestHostEnvironment(Environments.Production));

        var result = validator.Validate(Options.DefaultName, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("only with", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsRemoteHttpHostsEvenInDevelopment()
    {
        var options = new WebhookDeliveryTransportOptions
        {
            Enabled = true,
            AllowedHosts = ["webhook.example.test"],
            AllowedSchemes = [Uri.UriSchemeHttp],
            AllowedPorts = [8080],
            AllowLocalHttpForDevelopment = true
        };
        var validator = new WebhookDeliveryTransportOptionsValidator(
            new TestHostEnvironment(Environments.Development));

        var result = validator.Validate(Options.DefaultName, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("loopback hosts", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsWildcardDestinationHosts()
    {
        var options = new WebhookDeliveryTransportOptions
        {
            Enabled = true,
            AllowedHosts = ["*.example.test"],
            AllowedSchemes = [Uri.UriSchemeHttps],
            AllowedPorts = [443]
        };
        var validator = new WebhookDeliveryTransportOptionsValidator(
            new TestHostEnvironment(Environments.Production));

        var result = validator.Validate(Options.DefaultName, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("exact", StringComparison.Ordinal));
    }

    private static HttpClient CreateTransport(
        Uri receiverAddress,
        out HttpWebhookDeliveryTransport transport,
        int maximumResponseBytes = 8_000,
        TimeSpan? requestTimeout = null,
        ILogger<HttpWebhookDeliveryTransport>? logger = null) =>
        CreateTransport(
            new WebhookDeliveryTransportOptions
            {
                Enabled = true,
                AllowedHosts = [receiverAddress.Host],
                AllowedSchemes = [Uri.UriSchemeHttp],
                AllowedPorts = [receiverAddress.Port],
                AllowLocalHttpForDevelopment = true,
                MaximumResponseBytes = maximumResponseBytes
            },
            out transport,
            new SystemWebhookDnsResolver(),
            requestTimeout,
            logger);

    private static HttpClient CreateTransport(
        WebhookDeliveryTransportOptions options,
        out HttpWebhookDeliveryTransport transport,
        IWebhookDnsResolver resolver,
        TimeSpan? requestTimeout = null,
        ILogger<HttpWebhookDeliveryTransport>? logger = null)
    {
        var wrappedOptions = Options.Create(options);
        var policy = new WebhookDestinationPolicy(
            wrappedOptions,
            new TestHostEnvironment(Environments.Development),
            resolver);
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectCallback = policy.ConnectAsync,
            ConnectTimeout = TimeSpan.FromSeconds(2),
            MaxResponseHeadersLength = 16,
            UseCookies = false,
            UseProxy = false
        };
        var client = new HttpClient(handler)
        {
            Timeout = requestTimeout ?? TimeSpan.FromSeconds(2)
        };
        transport = new HttpWebhookDeliveryTransport(
            client,
            wrappedOptions,
            policy,
            logger ?? NullLogger<HttpWebhookDeliveryTransport>.Instance);
        return client;
    }

    private static WebhookDeliveryRequest CreateRequest(
        Uri endpoint,
        string payload = "{\"event\":\"inventory\"}",
        Guid? eventId = null,
        long deliveryId = 1,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        var stableEventId = eventId ?? Guid.NewGuid();
        return new WebhookDeliveryRequest(
            SubscriptionId: 21,
            EndpointUrl: new Uri(endpoint, "/events").ToString(),
            EventType: "inventory.movement-recorded",
            EventId: stableEventId,
            PayloadJson: payload,
            Headers: headers ?? WebhookSignature.CreateHeaders(
                Secret,
                payload,
                DateTimeOffset.UtcNow,
                stableEventId,
                deliveryId,
                "inventory.movement-recorded",
                1),
            DeliveryId: deliveryId,
            PayloadVersion: 1);
    }

    private sealed class FixedWebhookDnsResolver(IPAddress[] addresses) : IWebhookDnsResolver
    {
        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken) =>
            Task.FromResult(addresses);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add(formatter(state, exception));
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Wms.Infrastructure.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class Receiver(WebApplication app, Uri baseAddress) : IAsyncDisposable
    {
        public Uri BaseAddress { get; } = baseAddress;

        public static async Task<Receiver> StartAsync(RequestDelegate handler)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development,
                ContentRootPath = AppContext.BaseDirectory
            });
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
            var app = builder.Build();
            app.Run(handler);
            await app.StartAsync();
            var addresses = app.Services
                .GetRequiredService<IServer>()
                .Features
                .Get<IServerAddressesFeature>()?
                .Addresses;
            var address = addresses?.SingleOrDefault()
                ?? throw new InvalidOperationException("The disposable HTTP receiver did not bind an address.");
            return new Receiver(app, new Uri(address));
        }

        public async ValueTask DisposeAsync()
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}
