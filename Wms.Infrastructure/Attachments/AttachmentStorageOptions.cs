using Microsoft.Extensions.Configuration;

namespace Wms.Infrastructure.Attachments;

public sealed class AttachmentStorageOptions
{
    public const string SectionName = "Wms:Attachments";

    public string Provider { get; init; } = "Local";

    public string RootPath { get; init; } = Path.Combine(AppContext.BaseDirectory, "attachments");

    public long MaximumFileSizeBytes { get; init; } = 10_000_000;

    public int MaximumAttachmentsPerReference { get; init; } = 20;

    public long MaximumRequestBodyBytes { get; init; } = 12_000_000;

    public bool RequireAntivirusScan { get; init; }

    public static AttachmentStorageOptions From(IConfiguration? configuration)
    {
        var provider = configuration?[SectionName + ":Provider"]?.Trim() ?? "Local";
        var configuredRoot = configuration?[SectionName + ":RootPath"]?.Trim();
        var healthRoot = configuration?["Wms:Health:StoragePath"]?.Trim();
        var root = string.IsNullOrWhiteSpace(configuredRoot)
            ? string.IsNullOrWhiteSpace(healthRoot)
                ? Path.Combine(AppContext.BaseDirectory, "attachments")
                : Path.Combine(healthRoot, "attachments")
            : configuredRoot;
        if (!Path.IsPathRooted(root))
        {
            root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, root));
        }

        var options = new AttachmentStorageOptions
        {
            Provider = provider,
            RootPath = root,
            MaximumFileSizeBytes = configuration?.GetValue(
                SectionName + ":MaximumFileSizeBytes",
                10_000_000L) ?? 10_000_000L,
            MaximumAttachmentsPerReference = configuration?.GetValue(
                SectionName + ":MaximumAttachmentsPerReference",
                20) ?? 20,
            MaximumRequestBodyBytes = configuration?.GetValue(
                SectionName + ":MaximumRequestBodyBytes",
                12_000_000L) ?? 12_000_000L,
            RequireAntivirusScan = configuration?.GetValue(
                SectionName + ":RequireAntivirusScan",
                false) ?? false
        };

        options.Validate();
        return options;
    }

    public void Validate()
    {
        if (!string.Equals(Provider, "Local", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(Provider, "External", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Wms:Attachments:Provider must be Local or External.");
        }

        if (string.IsNullOrWhiteSpace(RootPath))
        {
            throw new InvalidOperationException(
                "Wms:Attachments:RootPath is required for local attachment storage.");
        }

        if (MaximumFileSizeBytes is < 1 or > 100_000_000)
        {
            throw new InvalidOperationException(
                "Wms:Attachments:MaximumFileSizeBytes must be between 1 and 100000000.");
        }

        if (MaximumAttachmentsPerReference is < 1 or > 1_000)
        {
            throw new InvalidOperationException(
                "Wms:Attachments:MaximumAttachmentsPerReference must be between 1 and 1000.");
        }

        if (MaximumRequestBodyBytes < MaximumFileSizeBytes || MaximumRequestBodyBytes > 500_000_000)
        {
            throw new InvalidOperationException(
                "Wms:Attachments:MaximumRequestBodyBytes must cover the maximum file size and be no more than 500000000.");
        }
    }
}
