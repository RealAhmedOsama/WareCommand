using OpenTelemetry.Exporter;

namespace Wms.ASP.Telemetry;

public sealed class WmsTelemetryOptions
{
    public bool Enabled { get; init; } = true;

    public string ServiceName { get; init; } = "WareCommand.Wms.Web";

    public bool ConsoleExporterEnabled { get; init; }

    public bool OtlpExporterEnabled { get; init; }

    public Uri? OtlpEndpoint { get; init; }

    public OtlpExportProtocol OtlpProtocol { get; init; } = OtlpExportProtocol.Grpc;

    public double SamplingRatio { get; init; } = 1.0;

    public static WmsTelemetryOptions From(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var section = configuration.GetSection("Wms:Telemetry");
        var serviceName = FirstNonEmpty(
            Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME"),
            configuration["OTEL_SERVICE_NAME"],
            section["ServiceName"])
            ?? "WareCommand.Wms.Web";
        var standardEndpoint = FirstNonEmpty(
            configuration["OTEL_EXPORTER_OTLP_ENDPOINT"],
            Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT"));
        var endpointValue = FirstNonEmpty(
            section["Otlp:Endpoint"],
            standardEndpoint);
        Uri? endpoint = null;
        if (!string.IsNullOrWhiteSpace(endpointValue))
        {
            if (!Uri.TryCreate(endpointValue, UriKind.Absolute, out endpoint) ||
                endpoint.Scheme is not ("http" or "https"))
            {
                throw new InvalidOperationException(
                    "Wms:Telemetry:Otlp:Endpoint must be an absolute HTTP or HTTPS URI.");
            }
        }

        var otlpEnabled = !string.IsNullOrWhiteSpace(standardEndpoint)
            ? GetBoolean(
                FirstNonEmpty(
                    configuration["OTEL_EXPORTER_OTLP_ENABLED"],
                    Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENABLED")),
                true)
            : GetBoolean(section["Otlp:Enabled"], !string.IsNullOrWhiteSpace(endpointValue));
        var protocol = ParseProtocol(FirstNonEmpty(
            section["Otlp:Protocol"],
            configuration["OTEL_EXPORTER_OTLP_PROTOCOL"],
            Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_PROTOCOL")));
        var samplingRatio = ParseSamplingRatio(
            section["SamplingRatio"],
            configuration["OTEL_TRACES_SAMPLER_ARG"],
            Environment.GetEnvironmentVariable("OTEL_TRACES_SAMPLER_ARG"));

        return new WmsTelemetryOptions
        {
            Enabled = GetBoolean(section["Enabled"], true),
            ServiceName = NormalizeServiceName(serviceName),
            ConsoleExporterEnabled = GetBoolean(
                section["ConsoleExporterEnabled"],
                environment.IsDevelopment()),
            OtlpExporterEnabled = otlpEnabled,
            OtlpEndpoint = endpoint,
            OtlpProtocol = protocol,
            SamplingRatio = samplingRatio
        };
    }

    private static OtlpExportProtocol ParseProtocol(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "grpc" => OtlpExportProtocol.Grpc,
            "http/protobuf" or "http_protobuf" or "httpprotobuf" =>
                OtlpExportProtocol.HttpProtobuf,
            _ => throw new InvalidOperationException(
                "Wms:Telemetry:Otlp:Protocol must be 'grpc' or 'http/protobuf'.")
        };

    private static double ParseSamplingRatio(
        string? configuredValue,
        string? configurationEnvironmentValue,
        string? processEnvironmentValue)
    {
        var value = FirstNonEmpty(
            configuredValue,
            configurationEnvironmentValue,
            processEnvironmentValue);
        return double.TryParse(
                value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var ratio)
            ? Math.Clamp(ratio, 0, 1)
            : 1.0;
    }

    private static bool GetBoolean(string? value, bool fallback) =>
        bool.TryParse(value, out var result) ? result : fallback;

    private static string NormalizeServiceName(string value)
    {
        var normalized = value.Trim();
        return normalized.Length is 0 or > 128 ? "WareCommand.Wms.Web" : normalized;
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}
