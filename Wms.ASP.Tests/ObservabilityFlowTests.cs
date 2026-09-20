using System.Net;
using System.Net.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Wms.ASP.Health;
using Wms.ASP.Telemetry;
using Xunit;

namespace Wms.ASP.Tests;

public sealed class ObservabilityFlowTests(WareCommandWebApplicationFactory factory)
    : IClassFixture<WareCommandWebApplicationFactory>
{
    [Fact]
    public async Task LivenessAndReadinessAreAnonymousSeparateAndTerse()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using var live = await client.GetAsync("/health/live");
        using var ready = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal("Healthy", (await live.Content.ReadAsStringAsync()).Trim());
        Assert.Equal("Healthy", (await ready.Content.ReadAsStringAsync()).Trim());
        Assert.DoesNotContain("database", await ready.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("connection", await ready.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadinessStateFailsClosedUntilStartupCompletes()
    {
        var state = new WmsApplicationHealthState();
        var check = new WmsApplicationReadinessHealthCheck(state);

        var starting = await check.CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(
            HealthStatus.Unhealthy,
            starting.Status);

        state.MarkReady();
        var ready = await check.CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Healthy, ready.Status);
    }

    [Fact]
    public void StandardOtlpEndpointEnablesExporterAndUsesBoundedSampling()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Wms:Telemetry:Otlp:Enabled"] = "false",
                ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://collector:4317",
                ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf",
                ["OTEL_TRACES_SAMPLER_ARG"] = "0.25"
            })
            .Build();

        var options = WmsTelemetryOptions.From(
            configuration,
            new TestHostEnvironment("Testing"));

        Assert.True(options.OtlpExporterEnabled);
        Assert.Equal("http://collector:4317/", options.OtlpEndpoint!.AbsoluteUri);
        Assert.Equal(OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf, options.OtlpProtocol);
        Assert.Equal(0.25, options.SamplingRatio);
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "Wms.ASP.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
