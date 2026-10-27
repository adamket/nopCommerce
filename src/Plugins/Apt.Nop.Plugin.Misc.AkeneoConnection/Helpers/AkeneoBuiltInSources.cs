using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;

/// <summary>
/// Akeneo product fields that are not attributes (so <c>/attributes</c> never
/// returns them) but can still be mapped like one. Each is exposed to the
/// attribute-mapping UI and validation as a synthetic attribute definition;
/// <see cref="Services.AkeneoProductValueResolver"/> reads the actual value
/// from the product's root field.
/// </summary>
public static class AkeneoBuiltInSources
{
    public const string GroupCode = "akeneo_product_fields";

    public const string EnabledCode = "enabled";

    /// <summary>
    /// The product's enabled/disabled status. Product models have no status,
    /// so this only yields a value for products (variants and standalone).
    /// </summary>
    public static AkeneoAttributeDefinition Enabled => new()
    {
        Code = EnabledCode,
        Type = "pim_catalog_boolean",
        Group = GroupCode,
        Labels = new Dictionary<string, string>
        {
            ["en_US"] = "Enabled (product status)"
        },
        Localizable = false,
        Scopable = false
    };

    public const string CompleteCode = "complete";

    /// <summary>
    /// True when Akeneo rates the product 100% complete for the sync's channel and
    /// locale. Product models have no completeness, so this only yields a value
    /// for products; a template or condition that needs it blocks a product model.
    /// </summary>
    public static AkeneoAttributeDefinition Complete => new()
    {
        Code = CompleteCode,
        Type = "pim_catalog_boolean",
        Group = GroupCode,
        Labels = new Dictionary<string, string>
        {
            ["en_US"] = "Complete (channel and locale)"
        },
        Localizable = false,
        Scopable = false
    };

    public static IReadOnlyList<AkeneoAttributeDefinition> All => [Enabled, Complete];

    /// <summary>
    /// Whether the product is 100% complete for the channel and locale, or null when
    /// Akeneo returned no completeness for them (product models, or a channel or
    /// locale the product is not rated for).
    /// </summary>
    public static bool? IsComplete(AkeneoProductDefinition product, string channel, string locale)
    {
        var entry = product?.Completenesses?.FirstOrDefault(completeness =>
            string.Equals(completeness.Scope, channel?.Trim(), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(completeness.Locale, locale?.Trim(), StringComparison.OrdinalIgnoreCase));

        return entry?.Data is { } percent ? percent >= 100 : null;
    }

    /// <summary>
    /// Returns the built-in source for <paramref name="code"/>, or null when the
    /// code is an ordinary Akeneo attribute.
    /// </summary>
    public static AkeneoAttributeDefinition Find(string code) =>
        All.FirstOrDefault(source => string.Equals(
            source.Code,
            code?.Trim(),
            StringComparison.OrdinalIgnoreCase));
}
