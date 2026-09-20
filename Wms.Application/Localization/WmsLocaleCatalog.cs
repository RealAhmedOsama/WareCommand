using System.Globalization;

namespace Wms.Application.Localization;

public static class WmsLocaleCatalog
{
    public const string English = "en-US";
    public const string Arabic = "ar-SA";
    public const string DefaultLocale = English;

    private static readonly IReadOnlyDictionary<string, CultureInfo> Cultures =
        new Dictionary<string, CultureInfo>(StringComparer.OrdinalIgnoreCase)
        {
            [English] = CultureInfo.GetCultureInfo(English),
            [Arabic] = CultureInfo.GetCultureInfo(Arabic)
        };

    public static IReadOnlyCollection<string> SupportedLocaleNames => Cultures.Keys.ToArray();

    public static IReadOnlyCollection<CultureInfo> SupportedCultures => Cultures.Values.ToArray();

    public static bool IsSupported(string? locale) =>
        !string.IsNullOrWhiteSpace(locale) && Cultures.ContainsKey(locale.Trim());

    public static bool TryGetCulture(string? locale, out CultureInfo culture)
    {
        if (!string.IsNullOrWhiteSpace(locale) && Cultures.TryGetValue(locale.Trim(), out var supportedCulture))
        {
            culture = supportedCulture;
            return true;
        }

        culture = Cultures[DefaultLocale];
        return false;
    }

    public static string Normalize(string? locale) =>
        TryGetCulture(locale, out var culture) ? culture.Name : DefaultLocale;
}
