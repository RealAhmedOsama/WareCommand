using System.Globalization;
using System.Resources;
using Wms.Application.Localization;

namespace Wms.Application.Tests.Localization;

public sealed class WmsLocalizationResourceTests
{
    [Fact]
    public void SupportedLocalesExposeEnglishAndArabicWithExpectedDirections()
    {
        WmsLocaleCatalog.SupportedLocaleNames
            .Should()
            .BeEquivalentTo([WmsLocaleCatalog.English, WmsLocaleCatalog.Arabic]);

        WmsLocaleCatalog.TryGetCulture(WmsLocaleCatalog.English, out var english).Should().BeTrue();
        WmsLocaleCatalog.TryGetCulture(WmsLocaleCatalog.Arabic, out var arabic).Should().BeTrue();

        english.TextInfo.IsRightToLeft.Should().BeFalse();
        arabic.TextInfo.IsRightToLeft.Should().BeTrue();
        WmsLocaleCatalog.Normalize("ar-sa").Should().Be(WmsLocaleCatalog.Arabic);
        WmsLocaleCatalog.Normalize("fr-FR").Should().Be(WmsLocaleCatalog.DefaultLocale);
    }

    [Fact]
    public void EnglishAndArabicResourceSetsHaveIdenticalKeysAndValues()
    {
        var manager = new ResourceManager(typeof(WmsSharedResource));
        var english = manager.GetResourceSet(
            CultureInfo.GetCultureInfo(WmsLocaleCatalog.English),
            createIfNotExists: true,
            tryParents: true)!;
        var arabic = manager.GetResourceSet(
            CultureInfo.GetCultureInfo(WmsLocaleCatalog.Arabic),
            createIfNotExists: true,
            tryParents: true)!;

        var englishKeys = english.Cast<System.Collections.DictionaryEntry>()
            .Select(entry => (string)entry.Key)
            .ToHashSet(StringComparer.Ordinal);
        var arabicKeys = arabic.Cast<System.Collections.DictionaryEntry>()
            .Select(entry => (string)entry.Key)
            .ToHashSet(StringComparer.Ordinal);

        arabicKeys.Should().BeEquivalentTo(englishKeys);
        englishKeys.Should().NotBeEmpty();
        englishKeys.Should().AllSatisfy(key =>
            manager.GetString(key, CultureInfo.GetCultureInfo(WmsLocaleCatalog.English))
                .Should().NotBeNullOrWhiteSpace());
        englishKeys.Should().AllSatisfy(key =>
            manager.GetString(key, CultureInfo.GetCultureInfo(WmsLocaleCatalog.Arabic))
                .Should().NotBeNullOrWhiteSpace());
    }
}
