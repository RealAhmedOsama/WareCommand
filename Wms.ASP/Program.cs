using System.Data.Common;
using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Wms.Application.DependencyInjection;
using Wms.ASP.Health;
using Wms.ASP.Identity;
using Wms.Infrastructure.Database;
using Wms.Infrastructure.DependencyInjection;

namespace Wms.ASP;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Any(argument => string.Equals(argument, "--healthcheck", StringComparison.OrdinalIgnoreCase)))
        {
            return await RunHealthProbeAsync();
        }

        var builder = WebApplication.CreateBuilder(args);
        var databaseProvider = WmsDatabaseProviderParser.Parse(
            builder.Configuration["Wms:DatabaseProvider"]);
        var seedProfile = WmsSeedProfileResolver.Resolve(
            builder.Configuration["Wms:SeedProfile"],
            builder.Environment.IsDevelopment());
        var connectionString = ResolveConnectionString(builder.Configuration);

        ConfigureHost(builder);
        ConfigureDataProtection(builder);
        ConfigureForwardedHeaders(builder);

        builder.Services.AddControllersWithViews();
        builder.Services.AddWmsInfrastructure(
            connectionString,
            databaseProvider);
        builder.Services.AddWmsApplication();
        builder.Services.AddWareCommandIdentity(builder.Configuration, builder.Environment);
        builder.Services
            .AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
            .AddCheck<WmsDatabaseHealthCheck>("database", tags: ["ready"]);

        var app = builder.Build();

        await InitializeDatabaseAsync(app.Services, seedProfile);
        await InitializeIdentityAsync(app.Services);

        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Home/Error");
            app.UseHsts();
        }

        if (builder.Configuration.GetValue("ForwardedHeaders:Enabled", false))
        {
            app.UseForwardedHeaders();
        }

        if (builder.Configuration.GetValue("HttpsRedirection:Enabled", true))
        {
            app.UseHttpsRedirection();
        }

        app.UseStaticFiles();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapHealthChecks(
            "/health/live",
            new HealthCheckOptions
            {
                Predicate = check => check.Tags.Contains("live")
            });
        app.MapHealthChecks(
            "/health/ready",
            new HealthCheckOptions
            {
                Predicate = check => check.Tags.Contains("ready")
            });

        app.MapControllerRoute(
            "default",
            "{controller=Dashboard}/{action=Index}/{id?}");

        app.Run();
        return 0;
    }

    private static void ConfigureHost(WebApplicationBuilder builder)
    {
        var shutdownTimeoutSeconds = Math.Clamp(
            builder.Configuration.GetValue("Host:ShutdownTimeoutSeconds", 30),
            5,
            300);

        builder.Services.Configure<HostOptions>(options =>
        {
            options.ShutdownTimeout = TimeSpan.FromSeconds(shutdownTimeoutSeconds);
        });
    }

    private static void ConfigureDataProtection(WebApplicationBuilder builder)
    {
        var keyDirectory = builder.Configuration["DataProtection:KeyDirectory"];
        if (string.IsNullOrWhiteSpace(keyDirectory))
        {
            return;
        }

        builder.Services
            .AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(keyDirectory))
            .SetApplicationName("WareCommand");
    }

    private static void ConfigureForwardedHeaders(WebApplicationBuilder builder)
    {
        if (!builder.Configuration.GetValue("ForwardedHeaders:Enabled", false))
        {
            return;
        }

        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders =
                ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

            var configuredProxies = builder.Configuration
                .GetSection("ForwardedHeaders:KnownProxies")
                .GetChildren()
                .Select(section => section.Value)
                .Concat((builder.Configuration["ForwardedHeaders:KnownProxies"] ?? string.Empty)
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

            foreach (var configuredProxy in configuredProxies.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (IPAddress.TryParse(configuredProxy, out var proxyAddress))
                {
                    options.KnownProxies.Add(proxyAddress);
                }
            }
        });
    }

    private static string? ResolveConnectionString(ConfigurationManager configuration)
    {
        var configuredConnectionString = configuration.GetConnectionString("DefaultConnection");
        if (!string.IsNullOrWhiteSpace(configuredConnectionString))
        {
            return configuredConnectionString;
        }

        var passwordFile = configuration["Wms:PostgresPasswordFile"];
        if (string.IsNullOrWhiteSpace(passwordFile))
        {
            return configuredConnectionString;
        }

        if (!File.Exists(passwordFile))
        {
            throw new InvalidOperationException(
                $"The configured PostgreSQL password file '{passwordFile}' does not exist.");
        }

        var password = File.ReadAllText(passwordFile).TrimEnd('\r', '\n');
        if (string.IsNullOrEmpty(password))
        {
            throw new InvalidOperationException(
                $"The configured PostgreSQL password file '{passwordFile}' is empty.");
        }

        var connectionStringBuilder = new DbConnectionStringBuilder
        {
            ["Host"] = configuration["Wms:PostgresHost"] ?? "postgres",
            ["Port"] = configuration["Wms:PostgresPort"] ?? "5432",
            ["Database"] = configuration["Wms:PostgresDatabase"] ?? "warecommand",
            ["Username"] = configuration["Wms:PostgresUsername"] ?? "warecommand",
            ["Password"] = password
        };

        return connectionStringBuilder.ConnectionString;
    }

    private static async Task<int> RunHealthProbeAsync()
    {
        using var client = new HttpClient
        {
            BaseAddress = new Uri("http://127.0.0.1:8080"),
            Timeout = TimeSpan.FromSeconds(2)
        };

        try
        {
            using var response = await client.GetAsync("/health/ready");
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (HttpRequestException)
        {
            return 1;
        }
        catch (TaskCanceledException)
        {
            return 1;
        }
    }

    private static async Task InitializeDatabaseAsync(
        IServiceProvider services,
        WmsSeedProfile seedProfile)
    {
        await using var scope = services.CreateAsyncScope();
        var initializer = scope.ServiceProvider.GetRequiredService<IWmsDatabaseInitializer>();
        await initializer.InitializeAsync(seedProfile);
    }

    private static async Task InitializeIdentityAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var bootstrapper = scope.ServiceProvider.GetRequiredService<WmsIdentityBootstrapper>();
        await bootstrapper.EnsureBootstrapAdministratorAsync();
    }
}
