using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;

internal static class AkeneoMappingSourceCatalog
{
    // These are product payload fields supported by AkeneoProductValueResolver,
    // not attribute definitions returned by the Akeneo attribute API.
    public static bool IsProductField(string code) => ProductFields.Contains(code?.Trim() ?? string.Empty);

    private static readonly HashSet<string> ProductFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "sku", "identifier", "uuid", "code", "family", "family_variant", "familyVariant",
        "parent", "enabled", "complete", "categories"
    };

    public static IReadOnlyList<string> GetAttributeCodes(AkeneoAttributeMapping mapping,
        IReadOnlyList<AkeneoAttributeMappingFallbackSource> fallbacks,
        IAkeneoValueTemplateRenderer renderer)
    {
        IEnumerable<string> codes = mapping.ValueMode == AkeneoAttributeMappingValueMode.Template
            ? renderer.Validate(mapping.ValueTemplate).ReferencedAttributeCodes
                .Concat(AkeneoMappingCondition.Validate(mapping.ApplyWhen, renderer).ReferencedAttributeCodes)
            : new[] { mapping.AkeneoAttributeCode }
                .Concat((fallbacks ?? Array.Empty<AkeneoAttributeMappingFallbackSource>()).Select(row => row.AkeneoAttributeCode));
        return codes.Where(code => !string.IsNullOrWhiteSpace(code) && !IsProductField(code))
            .Select(code => code.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
