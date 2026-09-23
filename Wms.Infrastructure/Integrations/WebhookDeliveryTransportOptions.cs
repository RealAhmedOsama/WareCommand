using System.Globalization;
using System.Net;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Wms.Infrastructure.Integrations;

public sealed class WebhookDeliveryTransportOptions
{
    public const string SectionName = "Wms:Integrations:Webhooks";

    public bool Enabled { get; set; }

    public string[] AllowedHosts { get; set; } = [];

    public string[] AllowedSchemes { get; set; } = [Uri.UriSchemeHttps];

    public int[] AllowedPorts { get; set; } = [443];

    public bool AllowLocalHttpForDevelopment { get; set; }

    public int RequestTimeoutSeconds { get; set; } = 10;

    public int ConnectTimeoutSeconds { get; set; } = 5;

    public int MaximumRequestBodyBytes { get; set; } = 1_000_000;

    public int MaximumResponseBytes { get; set; } = 8_000;
}

public sealed class WebhookDeliveryTransportOptionsValidator(IHostEnvironment environment)
    : IValidateOptions<WebhookDeliveryTransportOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        WebhookDeliveryTransportOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var allowedHosts = options.AllowedHosts ?? [];
        var allowedSchemes = options.AllowedSchemes ?? [];
        var allowedPorts = options.AllowedPorts ?? [];
        var errors = new List<string>();
        if (allowedHosts.Length == 0 || allowedHosts.Any(host =>
                string.IsNullOrWhiteSpace(host) ||
                host.Contains('*') ||
                !WebhookHostName.TryNormalize(host, out _)))
        {
            errors.Add("At least one exact, valid Wms:Integrations:Webhooks:AllowedHosts entry is required.");
        }

        if (allowedSchemes.Length == 0 || allowedSchemes.Any(scheme =>
                string.IsNullOrWhiteSpace(scheme) ||
                !scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
                !scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add("AllowedSchemes must contain only https or http.");
        }

        var allowsHttp = allowedSchemes.Contains(
            Uri.UriSchemeHttp,
            StringComparer.OrdinalIgnoreCase);
        if (allowsHttp &&
            (!options.AllowLocalHttpForDevelopment || !environment.IsDevelopment()))
        {
            errors.Add("HTTP webhooks are allowed only with AllowLocalHttpForDevelopment in Development.");
        }

        if (allowsHttp && allowedHosts.Any(host => !IsLoopbackHost(host)))
        {
            errors.Add("HTTP webhooks may allow only localhost or loopback hosts.");
        }

        if (options.AllowLocalHttpForDevelopment && !environment.IsDevelopment())
        {
            errors.Add("AllowLocalHttpForDevelopment cannot be enabled outside Development.");
        }

        if (allowedPorts.Length == 0 ||
            allowedPorts.Any(port => port is < 1 or > 65_535) ||
            allowedPorts.Distinct().Count() != allowedPorts.Length)
        {
            errors.Add("AllowedPorts must contain unique ports between 1 and 65535.");
        }

        if (options.RequestTimeoutSeconds is < 1 or > 60)
        {
            errors.Add("RequestTimeoutSeconds must be between 1 and 60.");
        }

        if (options.ConnectTimeoutSeconds is < 1 or > 30)
        {
            errors.Add("ConnectTimeoutSeconds must be between 1 and 30.");
        }

        if (options.MaximumRequestBodyBytes is < 1 or > 1_000_000)
        {
            errors.Add("MaximumRequestBodyBytes must be between 1 and 1000000.");
        }

        if (options.MaximumResponseBytes is < 1 or > 8_000)
        {
            errors.Add("MaximumResponseBytes must be between 1 and 8000.");
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }

    private static bool IsLoopbackHost(string host) =>
        !string.IsNullOrWhiteSpace(host) &&
        WebhookHostName.TryNormalize(host, out var normalized) &&
        (normalized.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
         IPAddress.TryParse(normalized, out var address) && IPAddress.IsLoopback(address));
}

internal static class WebhookHostName
{
    public static bool TryNormalize(string value, out string normalized)
    {
        normalized = string.Empty;
        var host = value.Trim().TrimEnd('.');
        if (host.Length is 0 or > 253)
        {
            return false;
        }

        if (IPAddress.TryParse(host, out var address))
        {
            normalized = address.ToString().ToLowerInvariant();
            return true;
        }

        try
        {
            normalized = new IdnMapping().GetAscii(host).ToLower(CultureInfo.InvariantCulture);
            return Uri.CheckHostName(normalized) == UriHostNameType.Dns;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
