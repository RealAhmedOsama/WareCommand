using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wms.Application.Integrations;
using Wms.Application.Telemetry;

namespace Wms.Infrastructure.Integrations;

public static class WebhookHttpTelemetryOptions
{
    public static readonly HttpRequestOptionsKey<bool> SuppressAutomaticTracing =
        new("WareCommand.Webhook.SuppressAutomaticTracing");
}

public sealed class HttpWebhookDeliveryTransport(
    HttpClient httpClient,
    IOptions<WebhookDeliveryTransportOptions> options,
    WebhookDestinationPolicy destinationPolicy,
    ILogger<HttpWebhookDeliveryTransport> logger) : IWebhookDeliveryTransport
{
    private static readonly HashSet<string> AllowedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Content-Type",
        WebhookSignature.HeaderName,
        WebhookSignature.TimestampHeaderName,
        WebhookSignature.EventIdHeaderName,
        WebhookSignature.DeliveryIdHeaderName,
        WebhookSignature.EventTypeHeaderName,
        WebhookSignature.VersionHeaderName
    };

    private readonly WebhookDeliveryTransportOptions _options = options.Value;

    public WebhookDeliveryTransportCapability Capability { get; } = new(
        Enabled: true,
        Configured: true);

    public async Task<WebhookDeliveryResult> SendAsync(
        WebhookDeliveryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!destinationPolicy.TryValidateEndpoint(request.EndpointUrl, out var endpoint, out var endpointError))
        {
            return PermanentFailure(endpointError);
        }

        if (request.DeliveryId <= 0 || request.PayloadVersion <= 0 ||
            string.IsNullOrWhiteSpace(request.EventType) ||
            string.IsNullOrWhiteSpace(request.PayloadJson))
        {
            return PermanentFailure("webhook_request_invalid");
        }

        if (request.PayloadJson.Length > _options.MaximumRequestBodyBytes)
        {
            return PermanentFailure("webhook_request_body_too_large");
        }

        var payload = Encoding.UTF8.GetBytes(request.PayloadJson);
        if (payload.Length > _options.MaximumRequestBodyBytes)
        {
            return PermanentFailure("webhook_request_body_too_large");
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint);
        message.Options.Set(WebhookHttpTelemetryOptions.SuppressAutomaticTracing, true);
        message.Content = new ByteArrayContent(payload);
        if (!TryApplyHeaders(message, request.Headers))
        {
            return PermanentFailure("webhook_request_headers_invalid");
        }

        using var activity = WmsTelemetry.ActivitySource.StartActivity(
            "webhook.delivery",
            ActivityKind.Client);
        activity?.SetTag("http.request.method", "POST");
        activity?.SetTag("wms.webhook.subscription_id", request.SubscriptionId);
        activity?.SetTag("wms.webhook.event_id", request.EventId.ToString("D"));
        activity?.SetTag("wms.webhook.delivery_id", request.DeliveryId);
        activity?.SetTag("wms.webhook.event_type", request.EventType);

        try
        {
            using var response = await httpClient.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            var responseBody = await ReadBoundedResponseBodyAsync(
                response,
                _options.MaximumResponseBytes,
                cancellationToken);
            var responseStatus = (int)response.StatusCode;
            if (response.IsSuccessStatusCode)
            {
                var result = new WebhookDeliveryResult(
                    Succeeded: true,
                    Retryable: false,
                    ResponseStatusCode: responseStatus,
                    ResponseBody: responseBody);
                RecordOutcome(activity, result);
                return result;
            }

            var retryable = response.StatusCode is HttpStatusCode.RequestTimeout or
                HttpStatusCode.TooManyRequests ||
                responseStatus == 425 ||
                responseStatus >= 500;
            var failure = new WebhookDeliveryResult(
                Succeeded: false,
                Retryable: retryable,
                ResponseStatusCode: responseStatus,
                ResponseBody: responseBody,
                Error: retryable
                    ? "webhook_http_retryable_failure"
                    : "webhook_http_permanent_failure",
                RetryAfterUtc: GetRetryAfterUtc(response.Headers.RetryAfter));
            RecordOutcome(activity, failure);
            return failure;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            activity?.SetTag("error.type", "webhook_delivery_cancelled");
            activity?.SetStatus(ActivityStatusCode.Error);
            throw;
        }
        catch (OperationCanceledException)
        {
            var failure = new WebhookDeliveryResult(
                Succeeded: false,
                Retryable: true,
                Error: "webhook_request_timed_out");
            RecordOutcome(activity, failure);
            logger.LogWarning(
                "Webhook request timed out for subscription {SubscriptionId}, event {EventId}",
                request.SubscriptionId,
                request.EventId);
            return failure;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or SocketException or IOException)
        {
            var failure = new WebhookDeliveryResult(
                Succeeded: false,
                Retryable: true,
                Error: "webhook_connection_failed");
            RecordOutcome(activity, failure);
            logger.LogWarning(
                "Webhook connection failed for subscription {SubscriptionId}, event {EventId}; failure category {FailureCategory}",
                request.SubscriptionId,
                request.EventId,
                exception.GetType().Name);
            return failure;
        }
    }

    private static void RecordOutcome(Activity? activity, WebhookDeliveryResult result)
    {
        if (activity is null)
        {
            return;
        }

        if (result.ResponseStatusCode.HasValue)
        {
            activity.SetTag("http.response.status_code", result.ResponseStatusCode.Value);
        }

        activity.SetTag("wms.webhook.succeeded", result.Succeeded);
        activity.SetTag("wms.webhook.retryable", result.Retryable);
        if (result.Error is not null)
        {
            activity.SetTag("error.type", result.Error);
        }

        activity.SetStatus(result.Succeeded ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
    }

    private static bool TryApplyHeaders(
        HttpRequestMessage message,
        IReadOnlyDictionary<string, string> headers)
    {
        foreach (var (name, value) in headers)
        {
            if (!AllowedHeaders.Contains(name) ||
                value.Contains('\r') ||
                value.Contains('\n'))
            {
                return false;
            }

            if (name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                if (!MediaTypeHeaderValue.TryParse(value, out var contentType) ||
                    !string.Equals(contentType.MediaType, "application/json", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                message.Content!.Headers.ContentType = contentType;
                continue;
            }

            if (!message.Headers.TryAddWithoutValidation(name, value))
            {
                return false;
            }
        }

        return message.Content!.Headers.ContentType is not null &&
               message.Headers.Contains(WebhookSignature.HeaderName) &&
               message.Headers.Contains(WebhookSignature.TimestampHeaderName) &&
               message.Headers.Contains(WebhookSignature.EventIdHeaderName) &&
               message.Headers.Contains(WebhookSignature.DeliveryIdHeaderName) &&
               message.Headers.Contains(WebhookSignature.EventTypeHeaderName) &&
               message.Headers.Contains(WebhookSignature.VersionHeaderName);
    }

    private static async Task<string?> ReadBoundedResponseBodyAsync(
        HttpResponseMessage response,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (response.Content is null)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[maximumBytes];
        var totalRead = 0;
        while (totalRead < maximumBytes)
        {
            var read = await stream.ReadAsync(
                buffer.AsMemory(totalRead, maximumBytes - totalRead),
                cancellationToken);
            if (read == 0)
            {
                break;
            }

            totalRead += read;
        }

        return totalRead == 0
            ? null
            : Encoding.UTF8.GetString(buffer, 0, totalRead);
    }

    private static DateTimeOffset? GetRetryAfterUtc(RetryConditionHeaderValue? retryAfter)
    {
        if (retryAfter?.Date is { } date)
        {
            return date;
        }

        return retryAfter?.Delta is { } delta
            ? DateTimeOffset.UtcNow.Add(delta)
            : null;
    }

    private static WebhookDeliveryResult PermanentFailure(string error) => new(
        Succeeded: false,
        Retryable: false,
        Error: error);
}
