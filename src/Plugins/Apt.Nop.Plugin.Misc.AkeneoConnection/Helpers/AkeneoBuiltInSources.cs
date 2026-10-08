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

    public static IReadOnlyList<AkeneoAttributeDefinition> All => [Enabled];

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
