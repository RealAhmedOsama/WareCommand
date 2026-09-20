namespace Wms.Infrastructure.Database;

public enum WmsDatabaseProvider
{
    PostgreSql,
    Sqlite
}

public sealed class WmsDatabaseOptions(
    WmsDatabaseProvider provider,
    bool connectionStringConfigured = true)
{
    public WmsDatabaseProvider Provider { get; } = provider;

    /// <summary>
    /// Indicates that the composition root resolved a usable connection string without exposing it.
    /// </summary>
    public bool ConnectionStringConfigured { get; } = connectionStringConfigured;
}

public static class WmsDatabaseProviderParser
{
    public static WmsDatabaseProvider Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return WmsDatabaseProvider.PostgreSql;
        }

        if (Enum.TryParse<WmsDatabaseProvider>(value, ignoreCase: true, out var provider))
        {
            return provider;
        }

        throw new InvalidOperationException(
            $"Unsupported WMS database provider '{value}'. Use PostgreSql or Sqlite.");
    }
}
