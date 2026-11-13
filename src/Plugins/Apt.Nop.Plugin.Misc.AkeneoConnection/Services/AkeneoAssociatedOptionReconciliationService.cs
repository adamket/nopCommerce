using System.Text.Json;
using System.Transactions;
using System.Xml.Linq;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Nop.Core.Domain.Catalog;
using Nop.Services.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

/// <summary>
/// Replace-all for associated options is a batch operation: a single leaf cannot
/// establish which siblings are absent. Never deletes the associated products.
/// </summary>
public class AkeneoAssociatedOptionReconciliationService(
    IAkeneoApiClient apiClient,
    IProductService productService,
    IProductAttributeService attributeService,
    IAkeneoManagedRelationService managedRelations,
    IAkeneoAttributeValueCacheTracker cacheTracker,
    IAkeneoDeferredProductEvents productEvents,
    IAkeneoSyncItemLogService itemLogs)
{
    private readonly Dictionary<string, Observation> _observations = new(StringComparer.OrdinalIgnoreCase);
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
        if (!_enabled || !outcome.Success || outcome.ActionType == SyncItemActionType.Failed)
            return;

        var key = SourceKey(source);
        if (key != null)
            _observations[key] = new Observation(outcome.NopProductId,
                outcome.NopParentProductId, outcome.NopProductAttributeValueId,
                outcome.AssociatedOptionsVerified);
    }

    public async Task<int> ReconcileAsync(AkeneoProductBatchImportRequest request,
        AkeneoProductBatchImportResult result, IDictionary<string, Product> parentProducts,
        CancellationToken cancellationToken = default, Func<Task> renewLease = null)
    {
        if (!_enabled || !CanReconcile(request) || !result.IsAuthoritative)
            return 0;

        // Finish every remote inventory before deleting anything. A paging failure
        // must never leave a partially fetched branch looking like an empty one.
        var plans = new List<RemovalPlan>();
        var candidates = _observations.Values
            .Where(item => item.Verified && item.ParentId > 0 && item.ValueId > 0)
            .GroupBy(item => item.ParentId.Value)
            .ToList();
        const string checkStep = "Checking associated options against Akeneo";
        var checkedCount = 0;

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            request.Steps?.Report(checkStep, "grouped products", checkedCount++, candidates.Count);
            var modelCodes = parentProducts.Where(pair => pair.Value?.Id == candidate.Key)
                .Select(pair => pair.Key).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (modelCodes.Count != 1)
            {
                PreserveParent(result, candidate.Key, "its Akeneo parent binding is ambiguous");
                continue;
            }

            var sourceKeys = await GetBranchInventoryAsync(modelCodes[0], cancellationToken, renewLease);
            if (sourceKeys.Count == 0 || sourceKeys.Any(key =>
                    !_observations.TryGetValue(key, out var observation) || observation.ProductId <= 0))
            {
                PreserveParent(result, candidate.Key,
                    "the complete Akeneo branch includes variants outside this run or skipped without an existing binding");
                continue;
            }

            var protectedProducts = sourceKeys.Select(key => _observations[key].ProductId).ToHashSet();
            var protectedValues = _observations.Values.Where(item => item.ValueId.HasValue)
                .Select(item => item.ValueId.Value).ToHashSet();
            var mappingIds = new HashSet<int>();
            foreach (var observation in candidate)
            {
                var option = await attributeService.GetProductAttributeValueByIdAsync(observation.ValueId.Value);
                if (option != null)
                    mappingIds.Add(option.ProductAttributeMappingId);
            }

            foreach (var mappingId in mappingIds)
            {
                var mapping = await attributeService.GetProductAttributeMappingByIdAsync(mappingId);
                if (mapping?.ProductId != candidate.Key)
                    continue;
                var options = await attributeService.GetProductAttributeValuesAsync(mappingId);
                var staleIds = options.Where(option =>
                        option.AttributeValueTypeId == (int)AttributeValueType.AssociatedToProduct &&
                        !protectedValues.Contains(option.Id) && !protectedProducts.Contains(option.AssociatedProductId))
                    .Select(option => option.Id).ToHashSet();
                if (staleIds.Count > 0)
                    plans.Add(new RemovalPlan(candidate.Key, mappingId, staleIds));
            }
        }

        request.Steps?.Report(checkStep, "grouped products", checkedCount, candidates.Count);

        const string removeStep = "Removing obsolete associated options";
        var removedCount = 0;
        var appliedCount = 0;
        foreach (var plan in plans)
        {
            request.Steps?.Report(removeStep, "product attributes", appliedCount++, plans.Count);
            if (renewLease != null)
                await renewLease();
            removedCount += await ApplyAsync(request, plan, cancellationToken);
        }
        request.Steps?.Report(removeStep, "product attributes", appliedCount, plans.Count);
        if (removedCount > 0)
            result.AddMessage($"Replace all values removed {removedCount} obsolete associated-product option(s); the simple products were preserved.");
        return removedCount;
    }

    private async Task<HashSet<string>> GetBranchInventoryAsync(string rootCode,
        CancellationToken cancellationToken, Func<Task> renewLease)
    {
        var sourceKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var modelCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { rootCode };
        var pendingModels = new Queue<string>();
        pendingModels.Enqueue(rootCode);
        while (pendingModels.TryDequeue(out var modelCode))
        {
            var search = JsonSerializer.Serialize(new
            {
                parent = new[] { new { @operator = "IN", value = new[] { modelCode } } }
            });
            foreach (var model in await ReadAllPagesAsync(search, true, cancellationToken, renewLease))
            {
                if (string.IsNullOrWhiteSpace(model.Code) || !modelCodes.Add(model.Code) ||
                    !string.Equals(model.Parent, modelCode, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Associated-option cleanup received an invalid or repeated product-model branch.");
                pendingModels.Enqueue(model.Code);
            }
            foreach (var source in await ReadAllPagesAsync(search, false, cancellationToken, renewLease))
            {
                if (!string.Equals(source.Parent, modelCode, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Associated-option cleanup received a product outside the requested branch.");
                var key = SourceKey(source) ?? throw new InvalidOperationException(
                    "Associated-option cleanup received a product without a source identity.");
                sourceKeys.Add(key);
            }
        }
        return sourceKeys;
    }

    private async Task<List<AkeneoProductDefinition>> ReadAllPagesAsync(string search, bool models,
        CancellationToken cancellationToken, Func<Task> renewLease)
    {
        var products = new List<AkeneoProductDefinition>();
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        string cursor = null;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (renewLease != null)
                await renewLease();
            AkeneoProductPageResult page = models
                ? await apiClient.GetProductModelsPageAsync(100, cursor, search, cancellationToken)
                : await apiClient.GetProductsPageAsync(100, cursor, search, cancellationToken);
            if (page?.Items == null || (page.Items.Count == 0 && page.HasNextPage))
                throw new InvalidOperationException("Associated-option cleanup could not read a complete Akeneo inventory.");
            products.AddRange(page.Items);
            cursor = page.SearchAfter;
            if (!string.IsNullOrWhiteSpace(cursor) && !cursors.Add(cursor))
                throw new InvalidOperationException("Associated-option cleanup received a repeated paging cursor.");
        } while (!string.IsNullOrWhiteSpace(cursor));
        return products;
    }

    private async Task<int> ApplyAsync(AkeneoProductBatchImportRequest request, RemovalPlan plan,
        CancellationToken cancellationToken)
    {
        var removed = new List<ProductAttributeValue>();
        Product parent = null;
        try
        {
            using (var transaction = new TransactionScope(TransactionScopeOption.Required,
                       TransactionScopeAsyncFlowOption.Enabled))
            {
                cancellationToken.ThrowIfCancellationRequested();
                parent = await productService.GetProductByIdAsync(plan.ParentId);
                if (parent == null || parent.Deleted)
                    return 0;
                var combinations = await attributeService.GetAllProductAttributeCombinationsAsync(plan.ParentId);
                var usedIds = combinations.Where(combination => !string.IsNullOrWhiteSpace(combination.AttributesXml))
                    .SelectMany(combination => XDocument.Parse(combination.AttributesXml)
                        .Descendants("ProductAttributeValue").Elements("Value"))
                    .Select(element => element.Value).ToHashSet();
                foreach (var option in await attributeService.GetProductAttributeValuesAsync(plan.MappingId))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!plan.ValueIds.Contains(option.Id) || usedIds.Contains(option.Id.ToString()) ||
                        option.AttributeValueTypeId != (int)AttributeValueType.AssociatedToProduct)
                        continue;
                    cacheTracker.Touch(plan.MappingId);
                    await attributeService.DeleteProductAttributeValueAsync(option);
                    await managedRelations.DeleteByRelationEntityAsync(
                        AkeneoManagedRelationType.AssociatedProductAttributeValue, option.Id);
                    removed.Add(option);
                    await itemLogs.InsertAkeneoSyncItemLogAsync(new AkeneoSyncItemLog
                    {
                        SyncRunRecordId = request.SyncRunRecordId,
                        NopProductId = plan.ParentId,
                        ActionTypeId = (int)SyncItemActionType.Updated,
                        CreatedOnUtc = DateTime.UtcNow,
                        Message = $"Replace all values removed obsolete option '{option.Name}' (value {option.Id}); associated product {option.AssociatedProductId} was preserved."
                    });
                }
                if (removed.Count > 0)
                    await AkeneoAttributeValuePreselection.ApplyAsync(attributeService, plan.MappingId);
                transaction.Complete();
            }
        }
        finally
        {
            await cacheTracker.ClearTouchedAsync();
        }
        if (removed.Count > 0)
        {
            productEvents.Defer(parent);
            await productEvents.PublishAsync();
        }
        return removed.Count;
    }

    private static string SourceKey(AkeneoProductDefinition source) =>
        !string.IsNullOrWhiteSpace(source?.Uuid) ? "uuid:" + source.Uuid.Trim() :
        !string.IsNullOrWhiteSpace(source?.Identifier) ? "identifier:" + source.Identifier.Trim() : null;

    private static void PreserveParent(AkeneoProductBatchImportResult result, int parentId, string reason)
    {
        result.WarningCount++;
        result.AddItemWarnings(new[] { $"Associated options on product {parentId} were preserved because {reason}." });
    }

    private sealed record Observation(int ProductId, int? ParentId, int? ValueId, bool Verified);
    private sealed record RemovalPlan(int ParentId, int MappingId, HashSet<int> ValueIds);
}
