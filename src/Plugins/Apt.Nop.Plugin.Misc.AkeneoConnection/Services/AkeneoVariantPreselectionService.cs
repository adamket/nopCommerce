using System.Transactions;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Nop.Core.Domain.Catalog;
using Nop.Services.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

/// <summary>Chooses a parent default only after variant writes and collection cleanup finish.</summary>
public class AkeneoVariantPreselectionService(
    IAkeneoApiClient apiClient,
    IAkeneoFamilyMappingService families,
    IAkeneoProductSyncStateService states,
    IProductService products,
    IProductAttributeService attributes,
    IProductAttributeParser attributeParser,
    IAkeneoAttributeValueCacheTracker cache,
    IAkeneoDeferredProductEvents events,
    IAkeneoSyncItemLogService logs
)
{
    private readonly Dictionary<string, AkeneoProductImportResult> _observations = new(
        StringComparer.OrdinalIgnoreCase
    );
    private readonly HashSet<int> _createdParents = new();

    public void Begin()
    {
        _observations.Clear();
        _createdParents.Clear();
    }

    public void Observe(AkeneoProductDefinition source, AkeneoProductImportResult outcome)
    {
        var key = AkeneoVariantBranchReader.SourceKey(source);
        if (key == null || !outcome.Success || outcome.ActionType == SyncItemActionType.Failed)
            return;

        _observations[key] = outcome;
        foreach (
            var related in outcome.RelatedResults.Where(item =>
                item.ActionType == SyncItemActionType.Created
            )
        )
            _createdParents.Add(related.NopProductId);
    }

    public async Task<int> ApplyAsync(
        AkeneoProductBatchImportRequest request,
        AkeneoProductBatchImportResult result,
        CancellationToken cancellationToken = default,
        Func<Task> renewLease = null
    )
    {
        if (
            !result.IsAuthoritative
            || !string.IsNullOrWhiteSpace(request.SearchAfter)
            || request.RunMode == AkeneoRunMode.SingleProduct
        )
            return 0;

        var parents = _observations
            .Values.Where(item =>
                item.NopParentProductId > 0
                && item.VariantFamilyMappingId > 0
                && !string.IsNullOrWhiteSpace(item.VariantParentModelCode)
                && item.DestinationKind
                    is AkeneoProductDestinationKind.AssociatedProduct
                        or AkeneoProductDestinationKind.ProductAttributeCombination
            )
            .GroupBy(item => item.NopParentProductId.Value)
            .ToList();
        var changed = 0;
        foreach (var parent in parents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (renewLease != null)
                await renewLease();
            if (!request.UpdateExistingProducts && !_createdParents.Contains(parent.Key))
                continue;

            var familyIds = parent
                .Select(item => item.VariantFamilyMappingId.Value)
                .Distinct()
                .ToList();
            var modelCodes = parent
                .Select(item => item.VariantParentModelCode)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (familyIds.Count != 1 || modelCodes.Count != 1)
                continue;
            var family = await families.GetByIdAsync(familyIds[0]);
            if (family is not { Enabled: true, AutomaticallyPreselectVariant: true } ||
                !string.IsNullOrWhiteSpace(family.CombinationRoutingJson))
                continue;

            // Avoid an Akeneo inventory when the parent already has a selection.
            var existingValues = await ReadValuesAsync(parent.Key);
            if (existingValues.Count == 0 || existingValues.Any(value => value.IsPreSelected))
                continue;

            var branch = await new AkeneoVariantBranchReader(apiClient).ReadAsync(
                modelCodes[0],
                cancellationToken,
                renewLease
            );
            var bindings = await ReadBindingsAsync(branch, request, cancellationToken);
            if (bindings == null || bindings.Count == 0)
            {
                result.AddMessage(
                    $"Deferred variant preselection for product ID {parent.Key} because its Akeneo branch has variants that have not synchronized."
                );
                continue;
            }

            if (renewLease != null)
                await renewLease();
            if (await SelectAsync(parent.Key, family, bindings, request, cancellationToken))
                changed++;
        }
        if (changed > 0)
            result.AddMessage($"Preselected the first variant for {changed} parent product(s).");
        return changed;
    }

    private async Task<List<AkeneoProductImportResult>> ReadBindingsAsync(
        IReadOnlyList<AkeneoProductDefinition> branch,
        AkeneoProductBatchImportRequest request,
        CancellationToken cancellationToken
    )
    {
        var bindings = new List<AkeneoProductImportResult>();
        foreach (var source in branch)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (
                !_observations.TryGetValue(
                    AkeneoVariantBranchReader.SourceKey(source),
                    out var binding
                )
            )
            {
                // A delta can contain only some siblings. Previously synchronized siblings
                // still count, but a new or detached sibling keeps the parent pending.
                var state = request.SyncProfileId.HasValue
                    ? await states.GetBySourceAsync(
                        request.SyncProfileId.Value,
                        AkeneoEntityType.Product,
                        source.Identifier,
                        source.Uuid
                    )
                    : null;
                if (
                    state == null
                    || !string.Equals(state.AkeneoParentCode, source.Parent, StringComparison.OrdinalIgnoreCase)
                    || (
                        state.LifecycleStatusId != (int)AkeneoProductLifecycleStatus.Active
                        && state.LifecycleStatusId
                            != (int)AkeneoProductLifecycleStatus.CombinationUnavailable
                    )
                )
                    return null;
                binding = new AkeneoProductImportResult
                {
                    NopProductId = state.NopProductId,
                    NopParentProductId = state.NopParentProductId,
                    NopProductAttributeValueId = state.NopProductAttributeValueId,
                    NopProductAttributeCombinationId = state.NopProductAttributeCombinationId,
                    DestinationKind = (AkeneoProductDestinationKind)state.DestinationKindId,
                    CombinationUnavailable =
                        state.LifecycleStatusId
                        == (int)AkeneoProductLifecycleStatus.CombinationUnavailable,
                };
            }
            if (
                binding.NopProductId <= 0
                || await products.GetProductByIdAsync(binding.NopProductId)
                    is not { Deleted: false }
            )
                return null;
            if (
                !binding.CombinationUnavailable
                && binding.DestinationKind
                    == AkeneoProductDestinationKind.ProductAttributeCombination
                && (
                    !binding.NopProductAttributeCombinationId.HasValue
                    || (
                        await attributes.GetProductAttributeCombinationByIdAsync(
                            binding.NopProductAttributeCombinationId.Value
                        )
                    )?.ProductId != binding.NopProductId
                )
            )
                return null;
            if (
                binding.DestinationKind == AkeneoProductDestinationKind.AssociatedProduct
                && (
                    !binding.NopProductAttributeValueId.HasValue
                    || (
                        await attributes.GetProductAttributeValueByIdAsync(
                            binding.NopProductAttributeValueId.Value
                        )
                    )?.AssociatedProductId != binding.NopProductId
                )
            )
                return null;
            bindings.Add(binding);
        }
        return bindings;
    }

    private async Task<List<ProductAttributeValue>> ReadValuesAsync(int parentId)
    {
        var values = new List<ProductAttributeValue>();
        foreach (
            var mapping in await attributes.GetProductAttributeMappingsByProductIdAsync(parentId)
        )
            values.AddRange(await attributes.GetProductAttributeValuesAsync(mapping.Id));
        return values;
    }

    private async Task<bool> SelectAsync(
        int parentId,
        AkeneoFamilyMapping family,
        IReadOnlyList<AkeneoProductImportResult> bindings,
        AkeneoProductBatchImportRequest request,
        CancellationToken cancellationToken
    )
    {
        Product parent;
        try
        {
            using var transaction = new TransactionScope(
                TransactionScopeOption.Required,
                TransactionScopeAsyncFlowOption.Enabled
            );
            cancellationToken.ThrowIfCancellationRequested();
            parent = await products.GetProductByIdAsync(parentId);
            if (parent is not { Deleted: false })
                return false;
            var values = await ReadValuesAsync(parentId);
            if (values.Any(value => value.IsPreSelected))
                return false;
            var selected = await ChooseValuesAsync(parentId, family, bindings, values);
            if (selected.Count == 0)
                return false;
            foreach (var value in selected)
            {
                cancellationToken.ThrowIfCancellationRequested();
                cache.Touch(value.ProductAttributeMappingId);
                value.IsPreSelected = true;
                await attributes.UpdateProductAttributeValueAsync(value);
            }
            await logs.InsertAkeneoSyncItemLogAsync(
                new AkeneoSyncItemLog
                {
                    SyncRunRecordId = request.SyncRunRecordId,
                    NopProductId = parentId,
                    ActionTypeId = (int)SyncItemActionType.Updated,
                    CreatedOnUtc = DateTime.UtcNow,
                    Message =
                        $"Preselected variant after synchronization: {string.Join(", ", selected.Select(value => value.Name))}.",
                }
            );
            transaction.Complete();
        }
        finally
        {
            await cache.ClearTouchedAsync();
        }
        events.Defer(parent);
        await events.PublishAsync();
        return true;
    }

    private async Task<IReadOnlyList<ProductAttributeValue>> ChooseValuesAsync(
        int parentId,
        AkeneoFamilyMapping family,
        IReadOnlyList<AkeneoProductImportResult> bindings,
        IReadOnlyList<ProductAttributeValue> values
    )
    {
        var parentBindings = bindings
            .Where(item => item.NopParentProductId == parentId && !item.CombinationUnavailable)
            .ToList();
        var optionIds = parentBindings
            .Where(item => item.DestinationKind == AkeneoProductDestinationKind.AssociatedProduct)
            .Select(item => item.NopProductAttributeValueId)
            .ToHashSet();
        var combinationIds = parentBindings
            .Where(item =>
                item.DestinationKind == AkeneoProductDestinationKind.ProductAttributeCombination
            )
            .Select(item => item.NopProductAttributeCombinationId)
            .ToHashSet();
        if (optionIds.Count > 0 && combinationIds.Count == 0)
        {
            var options = values
                .Where(value =>
                    optionIds.Contains(value.Id)
                    && value.AttributeValueType == AttributeValueType.AssociatedToProduct
                )
                .ToList();
            if (options.Select(value => value.ProductAttributeMappingId).Distinct().Count() != 1)
                return Array.Empty<ProductAttributeValue>();
            return options
                .OrderBy(value => value.DisplayOrder)
                .ThenBy(value => value.Id)
                .Take(1)
                .ToList();
        }
        if (optionIds.Count > 0 || combinationIds.Count == 0)
            return Array.Empty<ProductAttributeValue>();

        var mappings = await attributes.GetProductAttributeMappingsByProductIdAsync(parentId);
        var axes = await families.GetAxisMappingsAsync(family.Id);
        var keyOrder = AkeneoVariantDisplayOrderKeys
            .Parse(family.VariantDisplayOrderKeys)
            .Select((key, index) => (key.AttributeCode, index))
            .ToDictionary(
                item => item.AttributeCode,
                item => item.index,
                StringComparer.OrdinalIgnoreCase
            );
        var mappingIds = axes.OrderBy(axis =>
                keyOrder.GetValueOrDefault(axis.AkeneoAttributeCode, int.MaxValue)
            )
            .ThenBy(axis => axis.DisplayOrder)
            .ThenBy(axis => axis.Id)
            .SelectMany(axis =>
                mappings
                    .Where(mapping => mapping.ProductAttributeId == axis.NopProductAttributeId)
                    .Select(mapping => mapping.Id)
            )
            .Distinct()
            .ToList();
        if (mappingIds.Count == 0)
            return Array.Empty<ProductAttributeValue>();

        IReadOnlyList<ProductAttributeValue> best = Array.Empty<ProductAttributeValue>();
        foreach (
            var combination in (await attributes.GetAllProductAttributeCombinationsAsync(parentId))
                .Where(item => combinationIds.Contains(item.Id))
                .OrderBy(item => item.Id)
        )
        {
            var parsed = await attributeParser.ParseProductAttributeValuesAsync(
                combination.AttributesXml
            );
            var candidate = parsed
                .Select(item => values.FirstOrDefault(value => value.Id == item.Id))
                .ToList();
            if (
                candidate.Count == 0
                || candidate.Any(value =>
                    value == null || !mappingIds.Contains(value.ProductAttributeMappingId)
                )
                || candidate
                    .GroupBy(value => value.ProductAttributeMappingId)
                    .Any(group => group.Count() != 1)
                || mappings.Any(mapping =>
                    mapping.IsRequired
                    && mappingIds.Contains(mapping.Id)
                    && candidate.All(value => value.ProductAttributeMappingId != mapping.Id)
                )
            )
                continue;
            if (best.Count == 0 || CompareDisplayOrder(candidate, best, mappingIds) < 0)
                best = candidate;
        }
        return best;
    }

    private static int CompareDisplayOrder(
        IReadOnlyList<ProductAttributeValue> left,
        IReadOnlyList<ProductAttributeValue> right,
        IReadOnlyList<int> mappingIds
    )
    {
        foreach (var mappingId in mappingIds)
        {
            var leftOrder =
                left.FirstOrDefault(value =>
                    value.ProductAttributeMappingId == mappingId
                )?.DisplayOrder
                ?? int.MaxValue;
            var rightOrder =
                right
                    .FirstOrDefault(value => value.ProductAttributeMappingId == mappingId)
                    ?.DisplayOrder
                ?? int.MaxValue;
            var comparison = leftOrder.CompareTo(rightOrder);
            if (comparison != 0)
                return comparison;
        }
        return 0;
    }
}
