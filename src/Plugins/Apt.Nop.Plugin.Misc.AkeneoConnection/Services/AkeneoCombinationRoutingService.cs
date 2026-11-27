using System.Text.Json;
using System.Xml.Linq;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Services.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

/// <summary>Routes one leaf to multiple combinations, retaining the existing conditional selectors.</summary>
public sealed class AkeneoCombinationRoutingService(
    IProductAttributeService attributes,
    IAkeneoManagedRelationService relations,
    IAkeneoProductValueResolver values,
    IAkeneoCombinationAvailabilityService availability,
    IAkeneoCombinationPictureSynchronizer pictures,
    IAkeneoNopEntityMappingService entityMappings,
    IAkeneoAttributeValueCacheTracker cache)
{
    public const string OwnershipPrefix = "combination-route:";
    public static string OwnershipCode(int familyId) => OwnershipPrefix + familyId;

    public async Task<AkeneoVariantSyncResult> ApplyAsync(Product parent, AkeneoVariantImportContext context,
        AkeneoVariantRelationshipSource source, AkeneoCombinationRouting configuration)
    {
        configuration.Validate();
        if (parent.Id != configuration.ParentProductId ||
            !string.Equals(context.SourceProduct?.Parent, configuration.ProductModelCode, StringComparison.OrdinalIgnoreCase))
            throw new NopException("Combination routing does not match this parent product/model. No combinations were changed.");
        if (context.SyncProfileId is not > 0 || context.Options.FamilyVariantImportConfigurationId is not > 0)
            throw new NopException("Combination routing requires a sync profile and a saved family mapping.");
        var sourceKey = context.AkeneoUuid ?? context.AkeneoIdentifier;
        if (string.IsNullOrWhiteSpace(sourceKey)) throw new NopException("Combination routing requires a variant identity.");
        var ownerCode = OwnershipCode(context.Options.FamilyVariantImportConfigurationId.Value);
        var sourcePrefix = sourceKey + "|";
        var owned = (await relations.GetByProductAsync(context.SyncProfileId.Value, parent.Id,
            AkeneoManagedRelationType.ProductAttributeCombination, ownerCode))
            .Where(relation => relation.AkeneoValueCode?.StartsWith(sourcePrefix, StringComparison.Ordinal) == true).ToList();
        var combinations = await attributes.GetAllProductAttributeCombinationsAsync(parent.Id);
        var result = new AkeneoVariantSyncResult
        {
            CombinationRoutingApplied = true,
            Mode = AkeneoVariantRelationshipMode.ProductAttributeCombinations, Source = source,
            NopProductId = parent.Id, NopParentProductId = parent.Id
        };
        // A held Published rule means no adoption, creation, update or deletion, even if the routes changed.
        if (context.ManageVariantsByEnabledStatus &&
            context.CombinationAvailabilitySource == AkeneoCombinationAvailabilitySource.PublishedRule && context.PublishedRuleHeld)
        {
            result.NopProductAttributeCombinationIds = combinations.Where(combination => owned.Any(relation =>
                relation.NopRelationEntityId == combination.Id)).Select(combination => combination.Id).ToList();
            if (result.NopProductAttributeCombinationIds.Count == 0 && context.ExistingProductAttributeCombinationId is int heldId &&
                combinations.Any(combination => combination.Id == heldId))
                result.NopProductAttributeCombinationIds.Add(heldId);
            return Complete(result);
        }
        var available = await availability.IsAvailableAsync(context);
        // Resolve and validate every selector before the first write. A missing attribute is not an empty membership list.
        var plans = await PlanAsync(parent.Id, context, configuration);
        ValidateExisting(plans, combinations, owned, configuration.AdoptExistingCombinations, context.Sku);
        foreach (var plan in plans.Where(plan => plan.Combination != null))
        {
            var owner = await relations.GetByRelationEntityAsync(context.SyncProfileId.Value,
                AkeneoManagedRelationType.ProductAttributeCombination, plan.Combination.Id);
            if (owner?.AkeneoAttributeCode?.StartsWith(OwnershipPrefix, StringComparison.Ordinal) == true &&
                (owner.AkeneoAttributeCode != ownerCode || owner.AkeneoValueCode?.StartsWith(sourcePrefix, StringComparison.Ordinal) != true))
                throw new NopException($"Route '{plan.Route.Key}' is owned by another routed variant or family.");
        }
        if (!available)
        {
            // Explicit adoption also covers already-existing combinations whose first PIM state is unpublished.
            foreach (var plan in plans.Where(plan => plan.Combination != null &&
                         !owned.Any(relation => relation.NopRelationEntityId == plan.Combination.Id)))
                owned.Add(await relations.UpsertAsync(context.SyncProfileId.Value, context.SyncRunRecordId, parent.Id,
                    AkeneoManagedRelationType.ProductAttributeCombination, plan.Combination.Id, ownerCode, sourcePrefix + plan.Route.Key));
        }

        foreach (var plan in plans.Where(_ => available))
        {
            if (string.IsNullOrWhiteSpace(plan.YearMapping.ConditionAttributeXml))
            {
                plan.YearMapping.ConditionAttributeXml = SelectionXml((plan.SelectorMapping.Id, plan.SelectorValue.Id));
                await attributes.UpdateProductAttributeMappingAsync(plan.YearMapping);
                cache.Touch(plan.YearMapping.Id);
                result.RelationshipChanged = true;
            }
            var yearValue = plan.YearValue;
            if (yearValue == null)
            {
                yearValue = new ProductAttributeValue
                {
                    ProductAttributeMappingId = plan.YearMapping.Id, Name = plan.Year,
                    AttributeValueTypeId = (int)AttributeValueType.Simple,
                    DisplayOrder = int.TryParse(plan.Year, out var yearOrder) ? yearOrder : 0
                };
                await attributes.InsertProductAttributeValueAsync(yearValue);
                cache.Touch(plan.YearMapping.Id);
                result.RelationshipChanged = true;
            }
            var xml = SelectionXml((plan.SelectorMapping.Id, plan.SelectorValue.Id), (plan.YearMapping.Id, yearValue.Id));
            var combination = plan.Combination;
            if (combination == null)
            {
                combination = new ProductAttributeCombination
                {
                    ProductId = parent.Id, AttributesXml = xml, Sku = context.Sku,
                    StockQuantity = context.StockQuantity ?? 0, OverriddenPrice = context.Price
                };
                await attributes.InsertProductAttributeCombinationAsync(combination);
                result.DestinationCreated = result.DestinationChanged = true;
            }
            else if (combination.Sku != context.Sku ||
                (context.StockQuantity.HasValue && combination.StockQuantity != context.StockQuantity.Value) ||
                (context.Price.HasValue && combination.OverriddenPrice != context.Price.Value))
            {
                combination.Sku = context.Sku;
                if (context.StockQuantity.HasValue) combination.StockQuantity = context.StockQuantity.Value;
                if (context.Price.HasValue) combination.OverriddenPrice = context.Price.Value;
                await attributes.UpdateProductAttributeCombinationAsync(combination);
                result.DestinationChanged = true;
            }
            await relations.UpsertAsync(context.SyncProfileId.Value, context.SyncRunRecordId, parent.Id,
                AkeneoManagedRelationType.ProductAttributeCombination, combination.Id, ownerCode, sourcePrefix + plan.Route.Key);
            result.NopProductAttributeCombinationIds.Add(combination.Id);
            if (result.Changed)
            {
                cache.Touch(plan.SelectorMapping.Id);
                cache.Touch(plan.YearMapping.Id);
            }
        }

        // Availability always removes owned routes. Membership/rule changes are independently configurable.
        if (!available || configuration.ReconcileManagedCombinations)
        {
            foreach (var relation in owned.Where(relation => !result.NopProductAttributeCombinationIds.Contains(relation.NopRelationEntityId)))
            {
                var obsolete = combinations.SingleOrDefault(combination => combination.Id == relation.NopRelationEntityId);
                if (obsolete != null)
                {
                    foreach (var mapping in await attributes.GetProductAttributeMappingsByProductIdAsync(parent.Id)) cache.Touch(mapping.Id);
                    await pictures.ReleaseCombinationAsync(obsolete.Id);
                    await attributes.DeleteProductAttributeCombinationAsync(obsolete);
                    await entityMappings.DeleteMappingsByNopEntityAsync(NopEntityType.ProductAttributeCombination, obsolete.Id);
                    await relations.DeleteByRelationEntityAsync(AkeneoManagedRelationType.ProductAttributeCombination, obsolete.Id);
                    result.RelationshipChanged = true;
                }
                else await relations.DeleteAsync(relation);
            }
        }
        if (available && !configuration.ReconcileManagedCombinations)
            foreach (var retained in combinations.Where(combination => owned.Any(relation => relation.NopRelationEntityId == combination.Id)))
                if (!result.NopProductAttributeCombinationIds.Contains(retained.Id)) result.NopProductAttributeCombinationIds.Add(retained.Id);
        // Selector values/conditions remain intact; they may also serve manually maintained combinations.
        return Complete(result);
    }

    private static AkeneoVariantSyncResult Complete(AkeneoVariantSyncResult result)
    {
        result.NopProductAttributeCombinationId = result.NopProductAttributeCombinationIds.Count == 0
            ? null : result.NopProductAttributeCombinationIds.Min();
        result.CombinationUnavailable = result.NopProductAttributeCombinationIds.Count == 0;
        return result;
    }

    private async Task<List<RoutePlan>> PlanAsync(int parentId, AkeneoVariantImportContext context, AkeneoCombinationRouting configuration)
    {
        var mappings = await attributes.GetProductAttributeMappingsByProductIdAsync(parentId);
        var plans = new List<RoutePlan>();
        foreach (var route in configuration.Routes)
        {
            var selectorMapping = RequireMapping(mappings, route.SelectorAttributeId);
            var yearMapping = RequireMapping(mappings, route.YearAttributeId);
            var selectors = (await attributes.GetProductAttributeValuesAsync(selectorMapping.Id))
                .Where(value => value.AttributeValueTypeId == (int)AttributeValueType.Simple && value.Name == route.SelectorValue).ToList();
            if (selectors.Count != 1) throw new NopException($"Route '{route.Key}': selector '{route.SelectorValue}' is missing or ambiguous.");
            var selector = selectors[0];
            if (!string.IsNullOrWhiteSpace(yearMapping.ConditionAttributeXml) &&
                !SameSelections(yearMapping.ConditionAttributeXml, SelectionXml((selectorMapping.Id, selector.Id))))
                throw new NopException($"Route '{route.Key}' conflicts with the year selector's existing condition.");
            if (!Matches(ReadCodes(context, route.WhenAttribute), route.WhenValue)) continue;
            if (!string.IsNullOrWhiteSpace(route.MembershipAttribute) &&
                !Matches(ReadCodes(context, route.MembershipAttribute), route.MembershipValue)) continue;
            var yearCodes = ReadCodes(context, route.AxisAttribute);
            if (yearCodes.Count != 1 || string.IsNullOrWhiteSpace(yearCodes[0]))
                throw new NopException($"Route '{route.Key}' requires one nonempty '{route.AxisAttribute}' value.");
            var year = yearCodes[0];
            var yearValues = (await attributes.GetProductAttributeValuesAsync(yearMapping.Id)).Where(value => value.Name == year).ToList();
            if (yearValues.Count > 1 || yearValues.Any(value => value.AttributeValueTypeId != (int)AttributeValueType.Simple))
                throw new NopException($"Route '{route.Key}' has an ambiguous or associated year value '{year}'.");
            plans.Add(new RoutePlan(route, selectorMapping, selector, yearMapping, year, yearValues.SingleOrDefault()));
        }
        if (plans.GroupBy(plan => (plan.SelectorValue.Id, plan.YearMapping.Id, plan.Year)).Any(group => group.Count() > 1))
            throw new NopException("Multiple routes produced the same attribute selection.");
        return plans;
    }

    private IReadOnlyList<string> ReadCodes(AkeneoVariantImportContext context, string attribute)
    {
        if (!values.TryGetValue(context.SourceProduct, attribute, out var resolved, context.Locale, context.Channel, context.Currency) ||
            resolved.RawData is not JsonElement raw || raw.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            throw new NopException($"Combination routing source '{attribute}' is missing. No combinations were changed.");
        return raw.ValueKind switch
        {
            JsonValueKind.Array => raw.EnumerateArray().Select(ReadCode).ToList(),
            JsonValueKind.String or JsonValueKind.Number => new[] { ReadCode(raw) },
            _ => throw new NopException($"Combination routing source '{attribute}' must contain codes or a scalar value.")
        };
    }

    private static string ReadCode(JsonElement code) => code.ValueKind switch
    {
        JsonValueKind.String => code.GetString(), JsonValueKind.Number => code.GetRawText(),
        _ => throw new NopException("Combination routing cannot use an object or null as an option code.")
    };
    private static bool Matches(IReadOnlyList<string> codes, string expected) => codes.Contains(expected, StringComparer.Ordinal);
    private static ProductAttributeMapping RequireMapping(IList<ProductAttributeMapping> mappings, int attributeId)
    {
        var matches = mappings.Where(mapping => mapping.ProductAttributeId == attributeId).ToList();
        if (matches.Count != 1) throw new NopException($"Routing destination attribute {attributeId} must occur exactly once on the parent.");
        return matches[0];
    }

    private static void ValidateExisting(List<RoutePlan> plans, IList<ProductAttributeCombination> combinations,
        List<AkeneoManagedRelation> owned, bool adopt, string sku)
    {
        foreach (var plan in plans.Where(plan => plan.YearValue != null))
        {
            var xml = SelectionXml((plan.SelectorMapping.Id, plan.SelectorValue.Id), (plan.YearMapping.Id, plan.YearValue.Id));
            var matches = combinations.Where(combination => SameSelections(combination.AttributesXml, xml)).ToList();
            if (matches.Count > 1) throw new NopException($"Route '{plan.Route.Key}' matches duplicate combinations. Resolve them before syncing.");
            plan.Combination = matches.SingleOrDefault();
            if (plan.Combination != null && !owned.Any(relation => relation.NopRelationEntityId == plan.Combination.Id) &&
                !string.Equals(plan.Combination.Sku, sku, StringComparison.OrdinalIgnoreCase))
                throw new NopException($"Route '{plan.Route.Key}' has the expected selections but belongs to another SKU.");
            if (plan.Combination != null && !owned.Any(relation => relation.NopRelationEntityId == plan.Combination.Id) && !adopt)
                throw new NopException($"Route '{plan.Route.Key}' matches an existing combination. Enable adoption to manage it.");
        }
    }

    internal static string SelectionXml(params (int MappingId, int ValueId)[] selections) =>
        new XElement("Attributes", selections.OrderBy(selection => selection.MappingId).Select(selection =>
            new XElement("ProductAttribute", new XAttribute("ID", selection.MappingId),
                new XElement("ProductAttributeValue", new XElement("Value", selection.ValueId))))).ToString(SaveOptions.DisableFormatting);

    internal static bool SameSelections(string first, string second)
    {
        // Strict parsing rejects malformed/duplicate selections rather than silently adopting them.
        static string Normalize(string xml) => string.Join(";", XDocument.Parse(xml).Root.Elements("ProductAttribute")
            .Select(attribute => $"{int.Parse(attribute.Attribute("ID").Value)}:{string.Join(",", attribute.Elements("ProductAttributeValue").SelectMany(value => value.Elements("Value")).Select(value => int.Parse(value.Value)).OrderBy(id => id))}")
            .OrderBy(selection => selection, StringComparer.Ordinal));
        try { return Normalize(first) == Normalize(second); }
        catch (Exception ex) when (ex is System.Xml.XmlException or FormatException or NullReferenceException or OverflowException)
        { throw new NopException("A combination contains invalid attribute XML. No routing changes were made."); }
    }

    private sealed record RoutePlan(AkeneoCombinationRoute Route, ProductAttributeMapping SelectorMapping,
        ProductAttributeValue SelectorValue, ProductAttributeMapping YearMapping, string Year, ProductAttributeValue YearValue)
    {
        public ProductAttributeCombination Combination { get; set; }
    }
}
