namespace Wms.Infrastructure.Database;

public enum WmsSeedProfile
{
    None,
    Reference,
    Demo
}

public static class WmsSeedProfileResolver
{
    public static WmsSeedProfile Resolve(string? configuredValue, bool isDevelopment)
    {
        if (!string.IsNullOrWhiteSpace(configuredValue))
        {
            if (Enum.TryParse<WmsSeedProfile>(configuredValue, ignoreCase: true, out var configuredProfile) &&
                Enum.IsDefined(configuredProfile))
            {
                return configuredProfile;
            }

            throw new InvalidOperationException(
                $"Unsupported WMS seed profile '{configuredValue}'. Use None, Reference, or Demo.");
        }

        return isDevelopment ? WmsSeedProfile.Demo : WmsSeedProfile.None;
    }
}
