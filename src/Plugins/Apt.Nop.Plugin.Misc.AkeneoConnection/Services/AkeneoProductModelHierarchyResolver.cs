using System.Text.Json;
using System.Text.Json.Nodes;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

/// <summary>
/// Resolves the nopCommerce parent model and inherited Akeneo values without
/// performing I/O. The batch coordinator remains responsible for loading and
/// caching the product-model chain before invoking this service.
/// </summary>
public class AkeneoProductModelHierarchyResolver
    : IAkeneoProductModelHierarchyResolver
{
    public AkeneoProductModelHierarchyResolution Resolve(
        AkeneoProductDefinition leaf,
        IReadOnlyList<AkeneoProductDefinition> ancestorsNearestFirst,
        AkeneoProductModelHierarchyMode mode)
    {
        ArgumentNullException.ThrowIfNull(leaf);

        ancestorsNearestFirst ??= Array.Empty<AkeneoProductDefinition>();

        var ancestors = ancestorsNearestFirst
            .Where(model => model != null)
            .ToList();

        var immediateParent = ancestors.FirstOrDefault();
        if (immediateParent == null)
        {
            throw new InvalidOperationException(
                "A product-model hierarchy cannot be resolved without an immediate parent model.");
        }

        var selectedIndex = mode == AkeneoProductModelHierarchyMode.RootProductModel
            ? ancestors.Count - 1
            : 0;

        var selectedParent = ancestors[selectedIndex];
        var parentAncestors = ancestors
            .Skip(selectedIndex + 1)
            .ToList();

        var leafWithInheritedValues = MergeWithAncestors(leaf, ancestors);
        var effectiveLeaf = mode == AkeneoProductModelHierarchyMode.RootProductModel
            ? leafWithInheritedValues
            : leaf;

        return new AkeneoProductModelHierarchyResolution
        {
            Mode = mode,
            ImmediateParentModel = immediateParent,
            ParentProductModel = selectedParent,
            EffectiveParentProductModel = MergeWithAncestors(
                selectedParent,
                parentAncestors),
            LeafWithInheritedValues = leafWithInheritedValues,
            EffectiveLeaf = effectiveLeaf,
            AncestorsNearestFirst = ancestors,
            IsFlattened = mode == AkeneoProductModelHierarchyMode.RootProductModel &&
                          ancestors.Count > 1
        };
    }

    private static AkeneoProductDefinition MergeWithAncestors(
        AkeneoProductDefinition item,
        IReadOnlyList<AkeneoProductDefinition> ancestorsNearestFirst)
    {
        if (ancestorsNearestFirst.Count == 0)
            return item;

        return CopyWithValues(
            item,
            MergeValues(item.Values, ancestorsNearestFirst));
    }

    /// <summary>
    /// Merges Akeneo value collections from nearest to farthest ancestor.
    /// Values on the child win. For a shared attribute, missing locale/scope
    /// entries are inherited instead of dropping the entire ancestor attribute.
    /// </summary>
    private static JsonElement MergeValues(
        JsonElement itemValues,
        IReadOnlyList<AkeneoProductDefinition> ancestorsNearestFirst)
    {
        var merged = itemValues.ValueKind == JsonValueKind.Object
            ? JsonNode.Parse(itemValues.GetRawText())?.AsObject() ?? new JsonObject()
            : new JsonObject();

        foreach (var ancestor in ancestorsNearestFirst)
        {
            if (ancestor.Values.ValueKind != JsonValueKind.Object)
                continue;

            foreach (var property in ancestor.Values.EnumerateObject())
            {
                var inheritedNode = JsonNode.Parse(property.Value.GetRawText());
                if (inheritedNode == null)
                    continue;

                if (!merged.TryGetPropertyValue(property.Name, out var currentNode) ||
                    currentNode == null)
                {
                    merged[property.Name] = inheritedNode;
                    continue;
                }

                if (currentNode is JsonArray currentEntries &&
                    inheritedNode is JsonArray inheritedEntries)
                {
                    MergeMissingLocaleScopeEntries(currentEntries, inheritedEntries);
                }
            }
        }

        return JsonSerializer.SerializeToElement(merged);
    }

    private static void MergeMissingLocaleScopeEntries(
        JsonArray currentEntries,
        JsonArray inheritedEntries)
    {
        var existingKeys = currentEntries
            .Where(node => node != null)
            .Select(BuildValueEntryKey)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var inheritedEntry in inheritedEntries)
        {
            if (inheritedEntry == null)
                continue;

            var key = BuildValueEntryKey(inheritedEntry);
            if (!existingKeys.Add(key))
                continue;

            currentEntries.Add(JsonNode.Parse(inheritedEntry.ToJsonString()));
        }
    }

    private static string BuildValueEntryKey(JsonNode node)
    {
        if (node is not JsonObject valueObject)
            return $"raw:{node.ToJsonString()}";

        var locale = ReadString(valueObject["locale"]);
        var scope = ReadString(valueObject["scope"]);

        return $"locale:{locale ?? "<null>"}\u001fscope:{scope ?? "<null>"}";
    }

    private static string ReadString(JsonNode node)
    {
        if (node == null)
            return null;

        return node is JsonValue value && value.TryGetValue<string>(out var text)
            ? text
            : node.ToJsonString();
    }

    private static AkeneoProductDefinition CopyWithValues(
        AkeneoProductDefinition source,
        JsonElement values) => new()
        {
            Uuid = source.Uuid,
            Identifier = source.Identifier,
            Code = source.Code,
            Family = source.Family,
            FamilyVariant = source.FamilyVariant,
            Parent = source.Parent,
            Enabled = source.Enabled,
            Categories = source.Categories?.ToList() ?? new List<string>(),
            Values = values
        };
}
