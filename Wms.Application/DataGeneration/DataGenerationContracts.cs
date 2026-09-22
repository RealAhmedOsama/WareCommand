using System.Security.Cryptography;
using System.Text;

namespace Wms.Application.DataGeneration;

public sealed record WmsDataGenerationProfile(
    string Name,
    string IntendedUse,
    int Warehouses,
    int LocationsPerWarehouse,
    int Items,
    int Suppliers,
    int Customers,
    int Documents,
    int InventoryRows,
    bool IncludeEdgeCases,
    bool IncludeIntegrationHistory);

public static class WmsDataGenerationProfiles
{
    public const string MinimalDevelopment = "minimal-development";
    public const string FullDemo = "full-demo";
    public const string EdgeCases = "edge-cases";
    public const string IntegrationTest = "integration-test";
    public const string LargePerformance = "large-performance";

    private static readonly IReadOnlyDictionary<string, WmsDataGenerationProfile> Profiles =
        new Dictionary<string, WmsDataGenerationProfile>(StringComparer.OrdinalIgnoreCase)
        {
            [MinimalDevelopment] = new(
                MinimalDevelopment, "Local feature development", 1, 8, 25, 3, 5, 10, 25, false, false),
            [FullDemo] = new(
                FullDemo, "Non-production demonstrations", 2, 40, 250, 15, 40, 150, 400, true, true),
            [EdgeCases] = new(
                EdgeCases, "Boundary and exception tests", 1, 12, 40, 5, 10, 80, 80, true, true),
            [IntegrationTest] = new(
                IntegrationTest, "Repeatable integration journeys", 2, 20, 100, 10, 20, 100, 200, true, true),
            [LargePerformance] = new(
                LargePerformance, "Explicit load/performance qualification", 10, 500, 10_000, 250, 2_000, 50_000, 100_000, true, true)
        };

    public static IReadOnlyCollection<WmsDataGenerationProfile> All => Profiles.Values.ToArray();

    public static bool TryGet(string name, out WmsDataGenerationProfile profile) =>
        Profiles.TryGetValue(name.Trim(), out profile!);
}

public sealed record DataGenerationRequest(
    string Profile,
    string Seed,
    int Scale = 1,
    string Environment = "Development",
    string Locale = "en-US",
    bool ResetExisting = false,
    string? ResetConfirmation = null);

public sealed record DataGenerationRecord(
    string EntityType,
    string ExternalKey,
    IReadOnlyDictionary<string, string> Values);

public sealed record DataGenerationPlan(
    string GeneratorVersion,
    string Profile,
    string IntendedUse,
    string SeedFingerprint,
    string Environment,
    string Locale,
    bool ResetExisting,
    IReadOnlyDictionary<string, int> EstimatedCounts,
    IReadOnlyList<DataGenerationRecord> PreviewRecords)
{
    public static DataGenerationPlan Create(DataGenerationRequest request)
    {
        var profile = ValidateAndGetProfile(request);
        var seed = request.Seed.Trim();
        var fingerprint = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(seed)))
            .ToLowerInvariant();
        var random = new DeterministicSequence(seed);
        var preview = new List<DataGenerationRecord>();
        var previewCount = Math.Min(12, Math.Max(3, profile.Warehouses * 2));
        for (var index = 0; index < previewCount; index++)
        {
            var warehouseCode = $"WH-{random.Next(100, 999)}-{index + 1:00}";
            var itemCode = $"SKU-{random.Next(10_000, 99_999)}";
            preview.Add(new DataGenerationRecord(
                "warehouse-item",
                $"{warehouseCode}:{itemCode}",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["warehouseCode"] = warehouseCode,
                    ["warehouseName"] = request.Locale.StartsWith("ar", StringComparison.OrdinalIgnoreCase)
                        ? $"مخزن {index + 1}"
                        : $"Warehouse {index + 1}",
                    ["sku"] = itemCode,
                    ["description"] = request.Locale.StartsWith("ar", StringComparison.OrdinalIgnoreCase)
                        ? $"صنف تجريبي {index + 1}"
                        : $"Generated item {index + 1}"
                }));
        }

        var multiplier = request.Scale;
        return new DataGenerationPlan(
            "1",
            profile.Name,
            profile.IntendedUse,
            fingerprint,
            request.Environment.Trim(),
            request.Locale.Trim(),
            request.ResetExisting,
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["warehouses"] = profile.Warehouses * multiplier,
                ["locations"] = profile.Warehouses * profile.LocationsPerWarehouse * multiplier,
                ["items"] = profile.Items * multiplier,
                ["suppliers"] = profile.Suppliers * multiplier,
                ["customers"] = profile.Customers * multiplier,
                ["documents"] = profile.Documents * multiplier,
                ["inventoryRows"] = profile.InventoryRows * multiplier,
                ["edgeCases"] = profile.IncludeEdgeCases ? multiplier : 0,
                ["integrationHistory"] = profile.IncludeIntegrationHistory ? profile.Documents * multiplier : 0
            },
            preview);
    }

    private static WmsDataGenerationProfile ValidateAndGetProfile(DataGenerationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!WmsDataGenerationProfiles.TryGet(request.Profile, out var profile))
        {
            throw new ArgumentException("The requested data-generation profile is not supported.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Seed) || request.Seed.Trim().Length > 200)
        {
            throw new ArgumentException("A bounded deterministic seed is required.", nameof(request));
        }

        if (request.Scale is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Scale must be between 1 and 100.");
        }

        var environment = request.Environment.Trim();
        if (environment.Equals("Production", StringComparison.OrdinalIgnoreCase) ||
            environment.Equals("Staging", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Data generation is disabled for production and staging environments.");
        }

        if (request.ResetExisting && !string.Equals(
                request.ResetConfirmation,
                "RESET-NON-PRODUCTION",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "ResetExisting requires the exact non-production confirmation token.");
        }

        return profile;
    }

    private sealed class DeterministicSequence
    {
        private ulong _state;

        public DeterministicSequence(string seed)
        {
            _state = BitConverter.ToUInt64(
                SHA256.HashData(Encoding.UTF8.GetBytes(seed)),
                0);
        }

        public int Next(int minimumInclusive, int maximumExclusive)
        {
            _state += 0x9E3779B97F4A7C15;
            var value = _state;
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EB;
            value ^= value >> 31;
            return minimumInclusive + (int)(value % (uint)(maximumExclusive - minimumInclusive));
        }
    }
}
