using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;

/// <summary>
/// A mapped product text value resolved in a secondary Akeneo locale, for the
/// nopCommerce language with the matching culture. A null <see cref="Value"/>
/// means Akeneo has no translation, so the localized value is removed and the
/// storefront shows the primary (English) value.
/// </summary>
public sealed record AkeneoLocalizedValue(
    NopTargetType TargetType,
    string TargetKey,
    string Locale,
    int LanguageId,
    string LanguageName,
    string Value,
    string SourceName);

/// <summary>
/// The product properties nopCommerce stores per language. Everything else
/// (prices, SKU, stock, Published, custom properties) is base-only.
/// </summary>
public static class AkeneoLocalizableTargets
{
    private static readonly HashSet<string> ProductFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "Name", "ShortDescription", "FullDescription"
    };

    private static readonly HashSet<string> SeoFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "MetaTitle", "MetaDescription", "MetaKeywords", "SeName"
    };

    public static bool IsLocalizable(AkeneoAttributeMapping mapping) =>
        mapping != null &&
        ((NopTargetType)mapping.NopTargetTypeId switch
        {
            NopTargetType.ProductField => ProductFields.Contains(mapping.NopTargetKey ?? string.Empty),
            NopTargetType.SeoField => SeoFields.Contains(mapping.NopTargetKey ?? string.Empty),
            _ => false
        });

    /// <summary>
    /// Akeneo locale codes use an underscore (es_US), nopCommerce cultures a
    /// hyphen (es-US).
    /// </summary>
    public static bool MatchesCulture(string akeneoLocale, string languageCulture) =>
        !string.IsNullOrWhiteSpace(akeneoLocale) &&
        !string.IsNullOrWhiteSpace(languageCulture) &&
        string.Equals(akeneoLocale.Trim().Replace('_', '-'), languageCulture.Trim(), StringComparison.OrdinalIgnoreCase);
}
