using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

/// <summary>The mapping sections of the version-one configuration export.</summary>
public sealed class AkeneoConfigurationImportDocument
{
    [JsonRequired] public ImportMetadata Metadata { get; set; }
    [JsonRequired] public List<FamilySection> FamilyMappings { get; set; }
    [JsonRequired] public List<AttributeSection> AttributeMappings { get; set; }
    [JsonRequired] public List<AkeneoAssetMapping> AssetMappings { get; set; }
    [JsonRequired] public List<AkeneoNopEntityMapping> CategoryMappings { get; set; }

    public sealed class ImportMetadata
    {
        public string Format { get; set; }
        public int FormatVersion { get; set; }
        public string PluginSystemName { get; set; }
    }

    public sealed class FamilySection
    {
        [JsonRequired] public AkeneoFamilyMapping Configuration { get; set; }
        [JsonRequired] public List<AkeneoFamilyVariantAxisMapping> AxisMappings { get; set; }
        [JsonRequired] public List<AkeneoFamilySubModelRule> SubModelRules { get; set; }
    }

    public sealed class AttributeSection
    {
        [JsonRequired] public AkeneoAttributeMapping Configuration { get; set; }
        [JsonRequired] public List<AkeneoAttributeMappingFallbackSource> FallbackSources { get; set; }
    }

    public static AkeneoConfigurationImportDocument Parse(string json)
    {
        // The export contains both numeric *Id fields and enum convenience
        // properties. Numeric IDs are authoritative, even in an edited export.
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            foreach (var property in info.Properties.Where(p => p.PropertyType.IsEnum).ToList())
                info.Properties.Remove(property);
        });
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            TypeInfoResolver = resolver
        };
        try
        {
            using var raw = JsonDocument.Parse(json);
            RejectDuplicateProperties(raw.RootElement);
            var document = JsonSerializer.Deserialize<AkeneoConfigurationImportDocument>(json, options);
            Require(document?.Metadata?.Format == "akeneo-connection-diagnostic-configuration" &&
                    document.Metadata.FormatVersion == 1 &&
                    document.Metadata.PluginSystemName == AkeneoConnectionConstants.SystemName,
                "Choose a version-one Akeneo Connection configuration export.");
            Require(document.FamilyMappings != null && document.AttributeMappings != null &&
                    document.AssetMappings != null && document.CategoryMappings != null,
                "All four mapping sections must be arrays. Use an empty array to clear a section.");
            document.Validate();
            return document;
        }
        catch (JsonException)
        {
            throw new ArgumentException("The JSON is invalid or a required mapping section is missing.");
        }
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                Require(names.Add(property.Name), "Duplicate JSON property: " + property.Name);
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectDuplicateProperties(item);
    }

    private void Validate()
    {
        var familyKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var section in FamilyMappings)
        {
            Require(section?.Configuration != null && section.AxisMappings != null && section.SubModelRules != null,
                "Each family needs configuration, axisMappings and subModelRules.");
            var f = section.Configuration;
            Require(!string.IsNullOrWhiteSpace(f.AkeneoFamilyCode), "A family code is required.");
            Require(familyKeys.Add(f.AkeneoFamilyCode.Trim() + "::" + f.AkeneoFamilyVariantCode?.Trim()),
                "Duplicate family/variant configuration.");
            EnumValue<AkeneoVariantRelationshipMode>(f.VariantRelationshipModeId);
            EnumValue<AkeneoProductModelHierarchyMode>(f.ProductModelHierarchyModeId);
            foreach (var axis in section.AxisMappings)
                Require(axis != null && !string.IsNullOrWhiteSpace(axis.AkeneoAttributeCode) &&
                        axis.NopProductAttributeId > 0 && axis.AkeneoVariantAxisLevel is 1 or 2,
                    "A family axis needs a source attribute, destination attribute and level 1 or 2.");
            foreach (var rule in section.SubModelRules)
            {
                Require(rule != null, "A submodel rule cannot be null.");
                EnumValue<AkeneoVariantRelationshipMode>(rule.VariantRelationshipOverrideModeId);
            }
        }
        var slots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var section in AttributeMappings)
        {
            Require(section?.Configuration != null && section.FallbackSources != null,
                "Each attribute mapping needs configuration and fallbackSources.");
            var a = section.Configuration;
            EnumValue<AkeneoAttributeMappingValueMode>(a.ValueModeId);
            EnumValue<AkeneoAttributeType>(a.AkeneoAttributeTypeId);
            EnumValue<NopTargetType>(a.NopTargetTypeId);
            EnumValue<AkeneoSpecificationMissingValueHandling>(a.SpecificationMissingValueHandlingId);
            Scope(a.EntityScopeId);
            Require(a.ValueMode == AkeneoAttributeMappingValueMode.Template
                    ? !string.IsNullOrWhiteSpace(a.MappingKey) && !string.IsNullOrWhiteSpace(a.ValueTemplate)
                    : !string.IsNullOrWhiteSpace(a.AkeneoAttributeCode),
                "An attribute source or a template with a stable mapping key is required.");
            Require(slots.Add((a.AkeneoFamilyCode?.Trim() ?? "") + "::" + AkeneoMappingHelper.GetSourceMappingCode(a)),
                "Duplicate attribute mapping slot in the same family scope.");
            if ((NopTargetType)a.NopTargetTypeId is NopTargetType.ProductField or NopTargetType.SeoField or NopTargetType.CustomProperty)
                Require(!string.IsNullOrWhiteSpace(a.NopTargetKey), "A destination field/key is required.");
            foreach (var fallback in section.FallbackSources)
            {
                Require(fallback != null && !string.IsNullOrWhiteSpace(fallback.AkeneoAttributeCode),
                    "A fallback source attribute is required.");
                EnumValue<AkeneoAttributeType>(fallback.AkeneoAttributeTypeId);
            }
        }
        // Apply the same source-slot override semantics as runtime before
        // checking whether two effective rows write the same scalar field.
        foreach (var family in AttributeMappings.Select(s => s.Configuration.AkeneoFamilyCode?.Trim() ?? "").Append("").Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var effective = new Dictionary<string, AkeneoAttributeMapping>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in AttributeMappings.Select(s => s.Configuration)
                         .Where(a => string.IsNullOrWhiteSpace(a.AkeneoFamilyCode)))
                effective[AkeneoMappingHelper.GetSourceMappingCode(a)] = a;
            if (family.Length > 0)
                foreach (var a in AttributeMappings.Select(s => s.Configuration)
                             .Where(a => string.Equals(a.AkeneoFamilyCode?.Trim(), family, StringComparison.OrdinalIgnoreCase)))
                    effective[AkeneoMappingHelper.GetSourceMappingCode(a)] = a;
            var targets = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in effective.Values.Where(a => (NopTargetType)a.NopTargetTypeId is
                         NopTargetType.ProductField or NopTargetType.SeoField or NopTargetType.CustomProperty))
            {
                var key = a.NopTargetTypeId + "::" + a.NopTargetKey.Trim();
                targets.TryGetValue(key, out var scopes);
                Require((scopes & a.EntityScopeId) == 0,
                    "Overlapping destination '" + a.NopTargetKey + "' in " + (family.Length == 0 ? "global" : family) + " mappings.");
                targets[key] = scopes | a.EntityScopeId;
            }
        }
        var assetKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in AssetMappings)
        {
            Require(a != null && !string.IsNullOrWhiteSpace(a.MappingKey) && !string.IsNullOrWhiteSpace(a.SourceAttributeCode),
                "An asset mapping needs a stable mapping key and source attribute.");
            Require(assetKeys.Add((a.AkeneoFamilyCode?.Trim() ?? "") + "::" + a.MappingKey.Trim()),
                "Duplicate asset mapping key in the same family scope.");
            EnumValue<AkeneoAssetSourceType>(a.SourceTypeId);
            EnumValue<AkeneoAssetDestinationType>(a.DestinationTypeId);
            EnumValue<AkeneoAssetStorageMode>(a.StorageModeId);
            Scope(a.EntityScopeId);
        }
        var categories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in CategoryMappings)
        {
            Require(c != null && c.AkeneoEntityTypeId == (int)AkeneoEntityType.Category &&
                    c.NopEntityTypeId == (int)NopEntityType.Category && c.NopEntityId > 0 &&
                    !string.IsNullOrWhiteSpace(c.AkeneoCode), "Only category-to-category mappings are allowed in categoryMappings.");
            Require(categories.Add(c.AkeneoCode.Trim()), "Duplicate category mapping.");
        }
    }

    private static void Scope(int value) => Require(value > 0 && (value & ~7) == 0, "Invalid product role scope.");
    private static void EnumValue<T>(int value) where T : struct, Enum =>
        Require(Enum.IsDefined(typeof(T), value), "Invalid " + typeof(T).Name + " value.");
    internal static void Require(bool condition, string message)
    {
        if (!condition) throw new ArgumentException(message);
    }
}
