using System.Transactions;
using System.Xml;
using System.Xml.Linq;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Nop.Core.Domain.Catalog;
using Nop.Services.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

/// <summary>Replace-all for combination axes requires the complete, successfully processed branch.</summary>
public class AkeneoCombinationReconciliationService(
    IAkeneoApiClient apiClient,
    IAkeneoFamilyMappingService families,
    IProductService products,
    IProductAttributeService attributes,
    IAkeneoManagedRelationService relations,
    IAkeneoNopEntityMappingService entityMappings,
    IAkeneoCombinationPictureSynchronizer pictures,
    IAkeneoAttributeValueCacheTracker cache,
    IAkeneoDeferredProductEvents events,
    IAkeneoSyncItemLogService logs)
{
    private readonly Dictionary<string, AkeneoProductImportResult> _observations = new(StringComparer.OrdinalIgnoreCase);
    private bool _enabled;

    public void Begin(AkeneoProductBatchImportRequest request)
    {
        _observations.Clear();
        _enabled = CanReconcile(request);
    }

    internal static bool CanReconcile(AkeneoProductBatchImportRequest request) =>
        request.SyncProfileId > 0 && request.UpdateExistingProducts &&
        request.ProductAttributeSyncMode == AkeneoCollectionSyncMode.ReplaceAll &&
        request.RunMode is AkeneoRunMode.Full or AkeneoRunMode.Resync &&
        string.IsNullOrWhiteSpace(request.SearchAfter);

    public void Observe(AkeneoProductDefinition source, AkeneoProductImportResult outcome)
    {
        var key = AkeneoVariantBranchReader.SourceKey(source);
        if (_enabled && key != null && outcome.Success && outcome.ActionType != SyncItemActionType.Failed)
            _observations[key] = outcome;
    }

    public async Task<int> ReconcileAsync(AkeneoProductBatchImportRequest request,
        AkeneoProductBatchImportResult result, CancellationToken cancellationToken = default,
        Func<Task> renewLease = null)
    {
        if (!_enabled || !CanReconcile(request) || !result.IsAuthoritative)
            return 0;

        var candidates = _observations.Values.Where(item => item.CombinationOptionsVerified &&
                item.NopParentProductId > 0 && item.VariantFamilyMappingId > 0 &&
                !string.IsNullOrWhiteSpace(item.VariantParentModelCode) &&
                item.DestinationKind == AkeneoProductDestinationKind.ProductAttributeCombination)
            .GroupBy(item => item.NopParentProductId.Value).ToList();
        var plans = new List<RemovalPlan>();
        var checkedCount = 0;
        const string checkStep = "Checking combinations against Akeneo";
        foreach (var parent in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            request.Steps?.Report(checkStep, "parent products", checkedCount++, candidates.Count);
            var familyIds = parent.Select(item => item.VariantFamilyMappingId.Value).Distinct().ToList();
            var modelCodes = parent.Select(item => item.VariantParentModelCode).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (familyIds.Count != 1 || modelCodes.Count != 1)
            {
                Preserve(result, parent.Key, "its family or parent-model binding is ambiguous");
                continue;
            }

            // Inventory every branch before deleting anything; remote paging errors abort cleanup.
            var branch = await new AkeneoVariantBranchReader(apiClient).ReadAsync(modelCodes[0], cancellationToken, renewLease);
            var bindings = new List<AkeneoProductImportResult>();
            foreach (var source in branch)
            {
                if (!_observations.TryGetValue(AkeneoVariantBranchReader.SourceKey(source), out var item) ||
                    !item.CombinationOptionsVerified || item.NopParentProductId != parent.Key ||
                    item.NopProductId != parent.Key || item.VariantFamilyMappingId != familyIds[0] ||
                    !string.Equals(item.VariantParentModelCode, modelCodes[0], StringComparison.OrdinalIgnoreCase) ||
                    item.DestinationKind != AkeneoProductDestinationKind.ProductAttributeCombination ||
                    (!item.CombinationUnavailable && item.NopProductAttributeCombinationId is not > 0))
                {
                    bindings.Clear();
                    break;
                }
                bindings.Add(item);
            }
            if (bindings.Count == 0)
            {
                Preserve(result, parent.Key, "the complete Akeneo branch was not verified in this run (filtered, skipped, unbound, or empty)");
                continue;
            }
            var family = await families.GetByIdAsync(familyIds[0]);
            if (family is not { Enabled: true })
                continue;
            var routing = Types.AkeneoCombinationRouting.Parse(family.CombinationRoutingJson);
            var axes = await families.GetAxisMappingsAsync(family.Id);
            var plan = routing == null
                ? await BuildPlanAsync(parent.Key, axes, bindings, result)
                : await BuildRoutingPlanAsync(parent.Key, family.Id, request.SyncProfileId.Value, routing, bindings);
            if (plan != null)
                plans.Add(plan);
        }
        request.Steps?.Report(checkStep, "parent products", checkedCount, candidates.Count);

        var removed = 0;
        var applied = 0;
        const string removeStep = "Removing obsolete combinations and axis values";
        foreach (var plan in plans)
        {
            cancellationToken.ThrowIfCancellationRequested();
            request.Steps?.Report(removeStep, "parent products", applied++, plans.Count);
            if (renewLease != null)
                await renewLease();
            removed += await ApplyAsync(request, result, plan, cancellationToken);
        }
        request.Steps?.Report(removeStep, "parent products", applied, plans.Count);
        if (removed > 0)
            result.AddMessage($"Replace all values removed {removed} obsolete combination(s) and unused axis value(s).");
        return removed;
    }

    private async Task<RemovalPlan> BuildRoutingPlanAsync(int parentId, int familyId, int profileId,
        Types.AkeneoCombinationRouting routing, IReadOnlyList<AkeneoProductImportResult> bindings)
    {
        if (!routing.ReconcileManagedCombinations || routing.ParentProductId != parentId) return null;
        var protectedIds = bindings.SelectMany(binding => binding.NopProductAttributeCombinationIds).ToHashSet();
        if (bindings.Any(binding => !binding.CombinationUnavailable && binding.NopProductAttributeCombinationIds.Count == 0)) return null;
        var combinations = await attributes.GetAllProductAttributeCombinationsAsync(parentId);
        if (!protectedIds.IsSubsetOf(combinations.Select(combination => combination.Id))) return null;
        var owned = await relations.GetByProductAsync(profileId, parentId, AkeneoManagedRelationType.ProductAttributeCombination,
            AkeneoCombinationRoutingService.OwnershipCode(familyId));
        var obsoleteIds = owned.Select(relation => relation.NopRelationEntityId).Where(id => !protectedIds.Contains(id)).ToHashSet();
        if (obsoleteIds.Count == 0) return null;
        // Retain conditional selector values, including values used by manual combinations.
        return new RemovalPlan(parentId, new HashSet<int>(), combinations.ToDictionary(combination => combination.Id,
            combination => combination.AttributesXml), obsoleteIds, new HashSet<int>());
    }

    private async Task<RemovalPlan> BuildPlanAsync(int parentId, IList<AkeneoFamilyVariantAxisMapping> axes,
        IReadOnlyList<AkeneoProductImportResult> bindings, AkeneoProductBatchImportResult result)
    {
        var mappings = await attributes.GetProductAttributeMappingsByProductIdAsync(parentId);
        var targetAttributeIds = axes.Select(axis => axis.NopProductAttributeId).ToHashSet();
        var targetMappings = mappings.Where(mapping => targetAttributeIds.Contains(mapping.ProductAttributeId)).ToList();
        if (targetMappings.Count == 0 || targetMappings.GroupBy(mapping => mapping.ProductAttributeId).Any(group => group.Count() != 1))
        {
            Preserve(result, parentId, "its axis attribute mappings are missing or ambiguous");
            return null;
        }
        var mappingIds = targetMappings.Select(mapping => mapping.Id).ToHashSet();
        var requiredIds = axes.Where(axis => axis.IsRequired).Select(axis => axis.NopProductAttributeId).ToHashSet();
        if (!requiredIds.IsSubsetOf(targetMappings.Select(mapping => mapping.ProductAttributeId)))
        {
            Preserve(result, parentId, "a required axis mapping is missing");
            return null;
        }
        var requiredMappingIds = targetMappings.Where(mapping => requiredIds.Contains(mapping.ProductAttributeId)).Select(mapping => mapping.Id).ToHashSet();
        var values = new List<ProductAttributeValue>();
        foreach (var mapping in targetMappings)
            values.AddRange(await attributes.GetProductAttributeValuesAsync(mapping.Id));
        var combinations = await attributes.GetAllProductAttributeCombinationsAsync(parentId);
        Dictionary<int, Dictionary<int, HashSet<int>>> selections;
        try
        {
            selections = combinations.ToDictionary(combination => combination.Id, combination => ReadSelections(combination.AttributesXml));
        }
        catch (Exception ex) when (ex is XmlException or FormatException or InvalidOperationException or ArgumentException)
        {
            Preserve(result, parentId, "a combination has unreadable attribute selections");
            return null;
        }
        bool IsInScope(Dictionary<int, HashSet<int>> selection) =>
            selection.Count > 0 && selection.Keys.All(mappingIds.Contains) && requiredMappingIds.IsSubsetOf(selection.Keys);
        var protectedIds = bindings.Where(item => !item.CombinationUnavailable)
            .Select(item => item.NopProductAttributeCombinationId.Value).ToHashSet();
        foreach (var id in protectedIds)
        {
            if (!selections.TryGetValue(id, out var selection) || !IsInScope(selection) ||
                selection.Any(pair => pair.Value.Any(valueId => !values.Any(value => value.Id == valueId &&
                    value.ProductAttributeMappingId == pair.Key && value.AttributeValueTypeId == (int)AttributeValueType.Simple))))
            {
                Preserve(result, parentId, "a current variant's combination or axis value is missing or outside the managed axes");
                return null;
            }
        }
        var obsolete = combinations.Where(combination => !protectedIds.Contains(combination.Id) && IsInScope(selections[combination.Id])).ToList();
        var obsoleteIds = obsolete.Select(combination => combination.Id).ToHashSet();
        var used = selections.Where(pair => !obsoleteIds.Contains(pair.Key)).SelectMany(pair => pair.Value.Values).SelectMany(ids => ids).ToHashSet();
        var unused = values.Where(value => value.AttributeValueTypeId == (int)AttributeValueType.Simple && !used.Contains(value.Id)).Select(value => value.Id).ToHashSet();
        if (obsolete.Count == 0 && unused.Count == 0)
            return null;
        return new RemovalPlan(parentId, mappingIds, combinations.ToDictionary(item => item.Id, item => item.AttributesXml), obsoleteIds, unused);
    }

    private async Task<int> ApplyAsync(AkeneoProductBatchImportRequest request, AkeneoProductBatchImportResult result,
        RemovalPlan plan, CancellationToken token)
    {
        var removed = 0;
        Product parent = null;
        try
        {
            using (var transaction = new TransactionScope(TransactionScopeOption.Required, TransactionScopeAsyncFlowOption.Enabled))
            {
                token.ThrowIfCancellationRequested();
                parent = await products.GetProductByIdAsync(plan.ParentId);
                if (parent == null || parent.Deleted)
                    return 0;
                var current = await attributes.GetAllProductAttributeCombinationsAsync(plan.ParentId);
                if (current.Count != plan.CombinationXml.Count || current.Any(item =>
                    !plan.CombinationXml.TryGetValue(item.Id, out var xml) || xml != item.AttributesXml))
                {
                    Preserve(result, plan.ParentId, "its combinations changed while cleanup was being planned");
                    return 0;
                }
                foreach (var mappingId in plan.MappingIds)
                    cache.Touch(mappingId);
                foreach (var combination in current.Where(item => plan.CombinationIds.Contains(item.Id)))
                {
                    token.ThrowIfCancellationRequested();
                    await pictures.ReleaseCombinationAsync(combination.Id, token);
                    await attributes.DeleteProductAttributeCombinationAsync(combination);
                    await relations.DeleteByRelationEntityAsync(AkeneoManagedRelationType.ProductAttributeCombination, combination.Id);
                    await entityMappings.DeleteMappingsByNopEntityAsync(NopEntityType.ProductAttributeCombination, combination.Id);
                    await LogAsync(request, plan.ParentId, $"Removed obsolete combination {combination.Id} (SKU '{combination.Sku}').");
                    removed++;
                }
                // Unrelated combinations keep every value they use, including shared axis values.
                var used = current.Where(item => !plan.CombinationIds.Contains(item.Id))
                    .SelectMany(item => ReadSelections(item.AttributesXml).Values).SelectMany(ids => ids).ToHashSet();
                foreach (var mappingId in plan.MappingIds)
                {
                    foreach (var value in await attributes.GetProductAttributeValuesAsync(mappingId))
                    {
                        token.ThrowIfCancellationRequested();
                        if (!plan.ValueIds.Contains(value.Id) || used.Contains(value.Id) || value.AttributeValueTypeId != (int)AttributeValueType.Simple)
                            continue;
                        await attributes.DeleteProductAttributeValueAsync(value);
                        await relations.DeleteByRelationEntityAsync(AkeneoManagedRelationType.ProductAttributeValue, value.Id);
                        await entityMappings.DeleteMappingsByNopEntityAsync(NopEntityType.ProductAttributeValue, value.Id);
                        await LogAsync(request, plan.ParentId, $"Removed unused axis value '{value.Name}' (value {value.Id}).");
                        removed++;
                    }
                }
                transaction.Complete();
            }
        }
        finally
        {
            await cache.ClearTouchedAsync();
        }
        if (removed > 0)
        {
            events.Defer(parent);
            await events.PublishAsync();
        }
        return removed;
    }

    private Task LogAsync(AkeneoProductBatchImportRequest request, int parentId, string message) =>
        logs.InsertAkeneoSyncItemLogAsync(new AkeneoSyncItemLog
        {
            SyncRunRecordId = request.SyncRunRecordId, NopProductId = parentId,
            ActionTypeId = (int)SyncItemActionType.Updated, CreatedOnUtc = DateTime.UtcNow,
            Message = "Replace all values: " + message
        });

    private static Dictionary<int, HashSet<int>> ReadSelections(string xml)
    {
        var document = XDocument.Parse(xml);
        if (document.Root?.Name != "Attributes")
            throw new FormatException("Invalid attributes root.");
        var result = new Dictionary<int, HashSet<int>>();
        foreach (var element in document.Root.Elements())
        {
            if (element.Name != "ProductAttribute" || !int.TryParse((string)element.Attribute("ID"), out var id) || id <= 0)
                throw new FormatException("Invalid attribute mapping.");
            var values = element.Elements("ProductAttributeValue").Elements("Value")
                .Select(value => int.Parse(value.Value)).ToHashSet();
            if (values.Count == 0 || values.Any(value => value <= 0) || !result.TryAdd(id, values))
                throw new FormatException("Invalid attribute values.");
        }
        return result;
    }

    private static void Preserve(AkeneoProductBatchImportResult result, int parentId, string reason)
    {
        result.WarningCount++;
        result.AddItemWarnings(new[] { $"Combinations and axis values on product {parentId} were preserved because {reason}." });
    }

    private sealed record RemovalPlan(int ParentId, HashSet<int> MappingIds,
        Dictionary<int, string> CombinationXml, HashSet<int> CombinationIds, HashSet<int> ValueIds);
}
