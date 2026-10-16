using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

/// <summary>
/// Discovers product leaves that must be reconciled because an ancestor
/// product model changed. Akeneo does not automatically return unchanged
/// descendants from the products endpoint, so a normal product-only delta
/// would otherwise miss inherited changes.
/// </summary>
public class AkeneoProductModelDeltaFanOutService(
    IAkeneoApiClient akeneoApiClient,
    IAkeneoAttributeMappingService attributeMappingService = null,
    IAkeneoValueTemplateRenderer templateRenderer = null)
    : IAkeneoProductModelDeltaFanOutService
{
    private const int MaximumPageSize = 100;
    private const int ParentCodeBatchSize = 50;

    private static readonly JsonSerializerOptions SearchJsonOptions = new()
    {
        PropertyNamingPolicy = null
    };

    public bool ShouldRun(AkeneoProductBatchImportRequest request) =>
        request != null &&
        request.RunMode == AkeneoRunMode.Delta &&
        ResolveUpdatedAfterUtc(request).HasValue &&
        request.ProductParentFilterMode != AkeneoProductParentFilterMode.SimpleProductsOnly;

    public string BuildDirectProductSearchJson(
        AkeneoProductBatchImportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var updatedAfterUtc = ResolveUpdatedAfterUtc(request);
        if (!updatedAfterUtc.HasValue)
            return request.SearchJson;

        var search = ParseSearch(request.SearchJson);
        RemoveUpdatedCriteria(search);
        AddUpdatedCriterion(search, updatedAfterUtc.Value, includeLinkedAssets: false);
        return SerializeSearch(search);
    }

    public string BuildLinkedAssetProductSearchJson(
        AkeneoProductBatchImportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var updatedAfterUtc = ResolveUpdatedAfterUtc(request);
        if (!updatedAfterUtc.HasValue)
            return request.SearchJson;

        var search = ParseSearch(request.SearchJson);
        RemoveUpdatedCriteria(search);
        AddUpdatedCriterion(search, updatedAfterUtc.Value, includeLinkedAssets: true);
        return SerializeSearch(search);
    }

    public string BuildDescendantProductSearchJson(
        AkeneoProductBatchImportRequest request,
        IEnumerable<string> parentProductModelCodes)
    {
        ArgumentNullException.ThrowIfNull(request);

        var parentCodes = NormalizeCodes(parentProductModelCodes);
        if (parentCodes.Count == 0)
            throw new ArgumentException(
                "At least one parent product-model code is required.",
                nameof(parentProductModelCodes));

        var search = ParseSearch(request.SearchJson);
        RemoveUpdatedCriteria(search);

        // The standard profile parent criterion (EMPTY / NOT EMPTY) describes
        // the broad product scope. For fan-out, the exact affected model codes
        // are more precise. Preserve only a custom parent criterion supplied in
        // AdditionalSearchJson, allowing administrators to further intersect
        // the fan-out scope when they intentionally configured one.
        search.Remove("parent");
        RestoreAdditionalParentCriteria(search, request.AdditionalSearchJson);
        AddCriterion(search, "parent", "IN", parentCodes);

        return SerializeSearch(search);
    }

    public async Task<AkeneoProductModelDeltaFanOutPlan> BuildPlanAsync(
        AkeneoProductBatchImportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var plan = new AkeneoProductModelDeltaFanOutPlan();
        if (!ShouldRun(request))
            return plan;

        var updatedAfterUtc = ResolveUpdatedAfterUtc(request);
        if (!updatedAfterUtc.HasValue)
            return plan;

        var pageSize = request.PageSize <= 0
            ? MaximumPageSize
            : Math.Min(request.PageSize, MaximumPageSize);
        var directChangedCodes = await ReadProductModelCodesAsync(
            BuildProductModelUpdatedSearchJson(updatedAfterUtc.Value, includeLinkedAssets: false),
            pageSize,
            plan,
            cancellationToken);

        foreach (var code in directChangedCodes)
        {
            plan.AffectedProductModelReasons[code] =
                AkeneoSyncInclusionReason.AncestorProductModelChange;
        }

        plan.DirectChangedProductModelCount = directChangedCodes.Count;

        if (request.IncludeLinkedAssetUpdates)
        {
            var includingLinkedAssetCodes = await ReadProductModelCodesAsync(
                BuildProductModelUpdatedSearchJson(updatedAfterUtc.Value, includeLinkedAssets: true),
                pageSize,
                plan,
                cancellationToken);

            foreach (var code in includingLinkedAssetCodes)
            {
                if (plan.AffectedProductModelReasons.ContainsKey(code))
                    continue;

                plan.AffectedProductModelReasons[code] =
                    AkeneoSyncInclusionReason.LinkedAssetChange;
                plan.LinkedAssetOnlyProductModelCount++;
            }
        }

        if (!plan.HasAffectedProductModels)
            return plan;

        var initiallyChangedCodes = new HashSet<string>(
            plan.AffectedProductModelReasons.Keys,
            StringComparer.OrdinalIgnoreCase);
        var descendantCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var frontier = initiallyChangedCodes.ToList();

        while (frontier.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var nextFrontier = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var parentBatch in frontier.Chunk(ParentCodeBatchSize))
            {
                var childSearchJson = BuildChildProductModelSearchJson(parentBatch);
                string searchAfter = null;

                while (true)
                {
                    var page = await akeneoApiClient.GetProductModelsPageAsync(
                        pageSize,
                        searchAfter,
                        childSearchJson,
                        cancellationToken);

                    if (page?.Items == null || page.Items.Count == 0)
                        break;

                    plan.ProductModelsRead += page.Items.Count;

                    foreach (var child in page.Items)
                    {
                        var childCode = child.Code?.Trim();
                        var parentCode = child.Parent?.Trim();

                        if (string.IsNullOrWhiteSpace(childCode) ||
                            string.IsNullOrWhiteSpace(parentCode) ||
                            !plan.AffectedProductModelReasons.TryGetValue(
                                parentCode,
                                out var inheritedReason))
                        {
                            continue;
                        }

                        plan.AffectedProductModelReasons.TryGetValue(
                            childCode,
                            out var existingReason);

                        var combinedReason = existingReason | inheritedReason;
                        if (combinedReason == existingReason)
                            continue;

                        plan.AffectedProductModelReasons[childCode] = combinedReason;
                        nextFrontier.Add(childCode);

                        if (!initiallyChangedCodes.Contains(childCode))
                            descendantCodes.Add(childCode);
                    }

                    if (!page.HasNextPage)
                        break;

                    searchAfter = page.SearchAfter;
                }
            }

            frontier = nextFrontier.ToList();
        }

        plan.DescendantProductModelCount = descendantCodes.Count;
        return plan;
    }

    public async Task<IReadOnlyList<AkeneoReferenceEntityRecordChange>> FindChangedReferenceEntityRecordsAsync(
        AkeneoProductBatchImportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Editing a record does not change the products that link it, so the
        // products endpoint's updated filter never returns them.
        var updatedAfterUtc = ResolveUpdatedAfterUtc(request);
        if (request.RunMode != AkeneoRunMode.Delta || !updatedAfterUtc.HasValue)
            return Array.Empty<AkeneoReferenceEntityRecordChange>();

        // Only attributes whose record *fields* a mapping reads can change a product
        // when a record is edited. With none, the phase makes no Akeneo calls.
        var watched = await GetWatchedReferenceEntityAttributesAsync(cancellationToken);
        if (watched.Count == 0)
            return Array.Empty<AkeneoReferenceEntityRecordChange>();

        var linkingAttributes = new List<(string AttributeCode, string EntityCode)>();
        foreach (var (attributeCode, mappedEntityCode) in watched)
        {
            var entityCode = mappedEntityCode;
            if (string.IsNullOrWhiteSpace(entityCode))
            {
                var attribute = await akeneoApiClient.GetAttributeByCodeAsync(
                    attributeCode,
                    cancellationToken);
                entityCode = attribute?.Type is "akeneo_reference_entity" or "akeneo_reference_entity_collection"
                    ? attribute.ReferenceDataName?.Trim()
                    : null;
            }

            if (!string.IsNullOrWhiteSpace(entityCode))
                linkingAttributes.Add((attributeCode, entityCode));
        }

        var changes = new List<AkeneoReferenceEntityRecordChange>();
        foreach (var entity in linkingAttributes.GroupBy(
                     attribute => attribute.EntityCode,
                     StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var recordCodes = await akeneoApiClient.GetUpdatedReferenceEntityRecordCodesAsync(
                entity.Key,
                updatedAfterUtc.Value,
                cancellationToken);
            if (recordCodes.Count == 0)
                continue;

            changes.AddRange(entity.Select(attribute =>
                new AkeneoReferenceEntityRecordChange(attribute.AttributeCode, entity.Key, recordCodes)));
        }

        return changes;
    }

    /// <summary>
    /// Product attribute code -> reference entity code (null when the mapping leaves it to
    /// Akeneo) for every attribute whose linked records a non-ignored mapping reads a field
    /// of: a mapping or fallback source with a reference-entity field, or a template token
    /// such as {plant_info.scientific_name}. Mappings that use only the record code are
    /// left out, since editing a record never changes its code.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, string>> GetWatchedReferenceEntityAttributesAsync(
        CancellationToken cancellationToken)
    {
        var watched = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (attributeMappingService == null)
            return watched;

        void Watch(string attributeCode, string entityCode)
        {
            var code = attributeCode?.Trim();
            if (string.IsNullOrWhiteSpace(code))
                return;
            var entity = string.IsNullOrWhiteSpace(entityCode) ? null : entityCode.Trim();
            if (!watched.TryGetValue(code, out var known) || known == null)
                watched[code] = entity;
        }

        var mappings = (await attributeMappingService.GetAllAkeneoAttributeMappingsAsync())
            .Where(mapping => (NopTargetType)mapping.NopTargetTypeId != NopTargetType.Ignore)
            .ToList();
        var activeIds = mappings.Select(mapping => mapping.Id).ToHashSet();

        foreach (var mapping in mappings)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if ((AkeneoAttributeMappingValueMode)mapping.ValueModeId == AkeneoAttributeMappingValueMode.Template)
            {
                if (templateRenderer == null || string.IsNullOrWhiteSpace(mapping.ValueTemplate))
                    continue;
                foreach (var field in templateRenderer.Validate(mapping.ValueTemplate).ReferenceFields ?? [])
                    Watch(field.AttributeCode, null);
            }
            else if (!string.IsNullOrWhiteSpace(mapping.AkeneoReferenceEntityAttributeCode))
            {
                Watch(mapping.AkeneoAttributeCode, mapping.AkeneoReferenceEntityCode);
            }
        }

        foreach (var fallback in await attributeMappingService.GetAllFallbackSourcesAsync())
        {
            if (activeIds.Contains(fallback.AttributeMappingId) &&
                !string.IsNullOrWhiteSpace(fallback.AkeneoReferenceEntityAttributeCode))
            {
                Watch(fallback.AkeneoAttributeCode, fallback.AkeneoReferenceEntityCode);
            }
        }

        return watched;
    }

    public string BuildReferenceEntityProductSearchJson(
        AkeneoProductBatchImportRequest request,
        string attributeCode,
        IEnumerable<string> recordCodes)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(attributeCode);

        var codes = NormalizeCodes(recordCodes);
        if (codes.Count == 0)
            throw new ArgumentException("At least one record code is required.", nameof(recordCodes));

        // Keep the profile's scope (family, categories, ...) but select by the
        // changed records instead of by product update time.
        var search = ParseSearch(request.SearchJson);
        RemoveUpdatedCriteria(search);
        search.Remove(attributeCode.Trim());
        AddCriterion(search, attributeCode.Trim(), "IN", codes);
        return SerializeSearch(search);
    }

    private async Task<HashSet<string>> ReadProductModelCodesAsync(
        string searchJson,
        int pageSize,
        AkeneoProductModelDeltaFanOutPlan plan,
        CancellationToken cancellationToken)
    {
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string searchAfter = null;

        while (true)
        {
            var page = await akeneoApiClient.GetProductModelsPageAsync(
                pageSize,
                searchAfter,
                searchJson,
                cancellationToken);

            if (page?.Items == null || page.Items.Count == 0)
                break;

            plan.ProductModelsRead += page.Items.Count;

            foreach (var productModel in page.Items)
            {
                var code = productModel.Code?.Trim();
                if (!string.IsNullOrWhiteSpace(code))
                    codes.Add(code);
            }

            if (!page.HasNextPage)
                break;

            searchAfter = page.SearchAfter;
        }

        return codes;
    }

    private static string BuildProductModelUpdatedSearchJson(
        DateTime updatedAfterUtc,
        bool includeLinkedAssets)
    {
        var search = new JsonObject();
        AddUpdatedCriterion(search, updatedAfterUtc, includeLinkedAssets);
        return SerializeSearch(search);
    }

    private static DateTime? ResolveUpdatedAfterUtc(
        AkeneoProductBatchImportRequest request)
    {
        var updatedAfterUtc = request.UpdatedAfterUtc;

        if (request.UpdatedSinceLastNDays.HasValue &&
            request.UpdatedSinceLastNDays.Value > 0)
        {
            var rollingCutoffUtc = DateTime.UtcNow.AddDays(
                -request.UpdatedSinceLastNDays.Value);

            updatedAfterUtc = updatedAfterUtc.HasValue
                ? new[] { updatedAfterUtc.Value, rollingCutoffUtc }.Max()
                : rollingCutoffUtc;
        }

        return updatedAfterUtc;
    }

    private static string BuildChildProductModelSearchJson(
        IEnumerable<string> parentCodes)
    {
        var search = new JsonObject();
        AddCriterion(search, "parent", "IN", NormalizeCodes(parentCodes));
        return SerializeSearch(search);
    }

    private static JsonObject ParseSearch(string searchJson)
    {
        if (string.IsNullOrWhiteSpace(searchJson))
            return new JsonObject();

        var node = JsonNode.Parse(searchJson);
        return node as JsonObject ??
            throw new InvalidOperationException("Akeneo search JSON must be a JSON object.");
    }

    private static void RemoveUpdatedCriteria(JsonObject search)
    {
        search.Remove("updated");
        search.Remove("updated_including_linked_entities");
        search.Remove("updated_including_linked_type");
    }

    private static void AddUpdatedCriterion(
        JsonObject search,
        DateTime updatedAfterUtc,
        bool includeLinkedAssets)
    {
        var value = updatedAfterUtc
            .ToUniversalTime()
            .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

        if (!includeLinkedAssets)
        {
            AddCriterion(search, "updated", ">", value);
            return;
        }

        AddCriterion(
            search,
            "updated_including_linked_entities",
            ">",
            value);
        AddCriterion(
            search,
            "updated_including_linked_type",
            "IN",
            new[] { "asset" });
    }

    private static void RestoreAdditionalParentCriteria(
        JsonObject search,
        string additionalSearchJson)
    {
        if (string.IsNullOrWhiteSpace(additionalSearchJson))
            return;

        var additionalNode = JsonNode.Parse(additionalSearchJson);
        if (additionalNode is not JsonObject additionalSearch ||
            !additionalSearch.TryGetPropertyValue("parent", out var parentNode) ||
            parentNode == null)
        {
            return;
        }

        search["parent"] = CloneNode(parentNode);
    }

    private static void AddCriterion(
        JsonObject search,
        string fieldName,
        string operatorName,
        object value = null)
    {
        var criterion = new JsonObject
        {
            ["operator"] = operatorName
        };

        if (value != null)
            criterion["value"] = JsonSerializer.SerializeToNode(value);

        if (!search.TryGetPropertyValue(fieldName, out var existingNode) ||
            existingNode is not JsonArray criteria)
        {
            criteria = new JsonArray();
            search[fieldName] = criteria;
        }

        criteria.Add(criterion);
    }

    private static List<string> NormalizeCodes(IEnumerable<string> codes) =>
        codes?
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string>();

    private static JsonNode CloneNode(JsonNode node) =>
        node == null ? null : JsonNode.Parse(node.ToJsonString());

    private static string SerializeSearch(JsonObject search) =>
        search.Count == 0 ? null : search.ToJsonString(SearchJsonOptions);
}
