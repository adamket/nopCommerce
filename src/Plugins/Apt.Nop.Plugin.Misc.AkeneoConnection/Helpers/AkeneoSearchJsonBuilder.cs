using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;

public class AkeneoProductSearchJsonBuilder
{
    public string Build(AkeneoProductBatchImportRequest request)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        var search = new JsonObject();

        AddFamilySearch(search, request);
        AddCategorySearch(search, request);
        AddProductGroupSearch(search, request);
        AddEnabledSearch(search, request);
        AddUpdatedSearch(search, request);
        AddParentSearch(search, request);

        MergeAdditionalSearchJson(search, request.AdditionalSearchJson);

        return search.Count == 0
            ? null
            : search.ToJsonString(new JsonSerializerOptions
            {
                PropertyNamingPolicy = null
            });
    }

    private static void AddFamilySearch(
        JsonObject search,
        AkeneoProductBatchImportRequest request)
    {
        var familyCodes = NormalizeCodes(request.AkeneoFamilyCodes);

        if (!familyCodes.Any())
            return;

        AddSearchCriterion(search, "family", "IN", familyCodes);
    }

    private static void AddCategorySearch(
        JsonObject search,
        AkeneoProductBatchImportRequest request)
    {
        var categoryCodes = NormalizeCodes(request.AkeneoCategoryCodes);

        switch (request.CategoryFilterMode)
        {
            case AkeneoCategoryFilterMode.None:
                return;

            case AkeneoCategoryFilterMode.Unclassified:
                AddSearchCriterion(search, "categories", "UNCLASSIFIED");
                return;

            case AkeneoCategoryFilterMode.In:
                AddSearchCriterionIfValuesExist(search, "categories", "IN", categoryCodes);
                return;

            case AkeneoCategoryFilterMode.InChildren:
                AddSearchCriterionIfValuesExist(search, "categories", "IN CHILDREN", categoryCodes);
                return;

            case AkeneoCategoryFilterMode.NotIn:
                AddSearchCriterionIfValuesExist(search, "categories", "NOT IN", categoryCodes);
                return;

            case AkeneoCategoryFilterMode.NotInChildren:
                AddSearchCriterionIfValuesExist(search, "categories", "NOT IN CHILDREN", categoryCodes);
                return;

            case AkeneoCategoryFilterMode.InOrUnclassified:
                AddSearchCriterionIfValuesExist(search, "categories", "IN OR UNCLASSIFIED", categoryCodes);
                return;
        }
    }

    private static void AddProductGroupSearch(
        JsonObject search,
        AkeneoProductBatchImportRequest request)
    {
        var groupCodes = NormalizeCodes(request.AkeneoProductGroupCodes);

        if (!groupCodes.Any())
            return;

        AddSearchCriterion(search, "groups", "IN", groupCodes);
    }

    private static void AddEnabledSearch(
        JsonObject search,
        AkeneoProductBatchImportRequest request)
    {
        switch (request.ProductEnabledFilter)
        {
            case AkeneoProductEnabledFilter.EnabledOnly:
                AddSearchCriterion(search, "enabled", "=", true);
                break;

            case AkeneoProductEnabledFilter.DisabledOnly:
                AddSearchCriterion(search, "enabled", "=", false);
                break;
        }
    }

    private static void AddUpdatedSearch(
        JsonObject search,
        AkeneoProductBatchImportRequest request)
    {
        var updatedAfterUtc = request.UpdatedAfterUtc;

        if (request.UpdatedSinceLastNDays.HasValue &&
            request.UpdatedSinceLastNDays.Value > 0)
        {
            var updatedSinceUtc = DateTime.UtcNow.AddDays(-request.UpdatedSinceLastNDays.Value);

            updatedAfterUtc = updatedAfterUtc.HasValue
                ? new[] { updatedAfterUtc.Value, updatedSinceUtc }.Max()
                : updatedSinceUtc;
        }

        if (!updatedAfterUtc.HasValue)
            return;

        var value = updatedAfterUtc.Value
            .ToUniversalTime()
            .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

        if (request.IncludeLinkedAssetUpdates)
        {
            AddSearchCriterion(
                search,
                "updated_including_linked_entities",
                ">",
                value);
            AddSearchCriterion(
                search,
                "updated_including_linked_type",
                "IN",
                new[] { "asset" });
            return;
        }

        AddSearchCriterion(search, "updated", ">", value);
    }

    private static void AddParentSearch(
        JsonObject search,
        AkeneoProductBatchImportRequest request)
    {
        switch (request.ProductParentFilterMode)
        {
            case AkeneoProductParentFilterMode.SimpleProductsOnly:
                AddSearchCriterion(search, "parent", "EMPTY");
                break;

            case AkeneoProductParentFilterMode.VariantProductsOnly:
                AddSearchCriterion(search, "parent", "NOT EMPTY");
                break;
        }
    }

    private static void AddSearchCriterionIfValuesExist(
        JsonObject search,
        string fieldName,
        string operatorName,
        IEnumerable<string> values)
    {
        var normalizedValues = NormalizeCodes(values);

        if (!normalizedValues.Any())
            return;

        AddSearchCriterion(search, fieldName, operatorName, normalizedValues);
    }

    private static void AddSearchCriterion(
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

    private static void MergeAdditionalSearchJson(
        JsonObject search,
        string additionalSearchJson)
    {
        if (string.IsNullOrWhiteSpace(additionalSearchJson))
            return;

        var additionalNode = JsonNode.Parse(additionalSearchJson);

        if (additionalNode is not JsonObject additionalSearch)
            throw new InvalidOperationException("Additional search JSON must be a JSON object.");

        foreach (var item in additionalSearch)
        {
            if (item.Value is JsonArray additionalArray)
            {
                if (!search.TryGetPropertyValue(item.Key, out var existingNode) ||
                    existingNode is not JsonArray existingArray)
                {
                    existingArray = new JsonArray();
                    search[item.Key] = existingArray;
                }

                foreach (var criterion in additionalArray)
                    existingArray.Add(CloneJsonNode(criterion));
            }
            else
            {
                search[item.Key] = CloneJsonNode(item.Value);
            }
        }
    }

    private static IList<string> NormalizeCodes(
        IEnumerable<string> codes)
    {
        return codes?
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string>();
    }

    private static JsonNode CloneJsonNode(
        JsonNode node)
    {
        return node == null
            ? null
            : JsonNode.Parse(node.ToJsonString());
    }
}