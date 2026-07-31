using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Nop.Core.Domain.Catalog;
using Nop.Services.Catalog;
using static Apt.Nop.Plugin.Misc.AkeneoConnection.Services.DryRun.AkeneoDryRunPlanHelper;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services.DryRun;

public sealed class AkeneoDryRunProductAttributePlanner(
    IProductAttributeService productAttributeService,
    IAkeneoNopEntityMappingService entityMappingService,
    IAkeneoManagedRelationService managedRelationService,
    IAkeneoFamilyMappingService familyMappingService)
    : IAkeneoDryRunSectionPlanner
{
    public int Order => 500;

    public async Task PlanAsync(
        AkeneoProductSyncContext context,
        AkeneoProductMappingPreviewModel model,
        CancellationToken cancellationToken = default)
    {
        if (context.Request.ProductAttributeSyncMode == AkeneoCollectionSyncMode.Disabled)
        {
            model.CoverageNotes.Add(
                "Product-attribute synchronization is disabled by the saved sync profile.");
            return;
        }

        var definitions = await productAttributeService.GetAllProductAttributesAsync();
        var product = context.ExistingProduct;
        var productMappings = product == null
            ? new List<ProductAttributeMapping>()
            : (await productAttributeService
                .GetProductAttributeMappingsByProductIdAsync(product.Id)).ToList();

        var variantAxisCodes = await GetVariantAxisCodesAsync(context.MappingFamilyCode);

        foreach (var mapped in context.GetMappings(NopTargetType.ProductAttribute))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var attributeCode = mapped.Mapping.AkeneoAttributeCode?.Trim() ?? string.Empty;
            var sourceName = GetSourceName(mapped);
            var attributeId = mapped.Mapping.NopTargetEntityId ?? 0;
            var definition = definitions.FirstOrDefault(item => item.Id == attributeId);

            if (variantAxisCodes.Contains(attributeCode))
            {
                AddOperation(
                    model,
                    "Product attributes",
                    definition?.Name ?? attributeCode,
                    sourceName,
                    null,
                    null,
                    AkeneoDryRunOperationType.Skip,
                    "This Akeneo attribute is a configured variant axis. Variant relationship synchronization owns it.",
                    mapped.Mapping.IsRequired);
                continue;
            }

            if (attributeId <= 0 || definition == null)
            {
                AddReviewOperation(
                    model,
                    "Product attributes",
                    attributeId > 0 ? $"Product attribute #{attributeId}" : "Missing target",
                    sourceName,
                    "The target nopCommerce product attribute is missing.",
                    mapped.Mapping.IsRequired);
                continue;
            }

            var productMapping = productMappings.FirstOrDefault(item =>
                item.ProductAttributeId == attributeId);

            var desiredItems = mapped.HasValue
                ? AkeneoSyncValueHelper.GetOptionItems(mapped)
                : Array.Empty<AkeneoResolvedOptionItem>();

            if (productMapping == null && desiredItems.Count > 0)
            {
                AddOperation(
                    model,
                    "Product attributes",
                    $"{definition.Name} mapping",
                    sourceName,
                    "Not configured on product",
                    "Create dropdown mapping",
                    AkeneoDryRunOperationType.Add,
                    "A ProductAttributeMapping would be created before its values are synchronized.",
                    mapped.Mapping.IsRequired);
            }

            if (productMapping == null)
            {
                foreach (var item in desiredItems)
                {
                    var displayName = item.DisplayName?.Trim() ?? item.AkeneoOptionCode?.Trim();
                    if (string.IsNullOrWhiteSpace(displayName))
                        continue;

                    AddOperation(
                        model,
                        "Product attributes",
                        $"{definition.Name}: {displayName}",
                        sourceName,
                        "Value absent",
                        context.Request.CreateMissingProductAttributeValues
                            ? "Create value"
                            : "Cannot create value",
                        context.Request.CreateMissingProductAttributeValues
                            ? AkeneoDryRunOperationType.Add
                            : AkeneoDryRunOperationType.Review,
                        context.Request.CreateMissingProductAttributeValues
                            ? "The dropdown value would be created."
                            : "The profile does not allow missing product attribute values to be created.",
                        mapped.Mapping.IsRequired);
                }

                continue;
            }

            if (productMapping.AttributeControlTypeId != (int)AttributeControlType.DropdownList ||
                productMapping.IsRequired != mapped.Mapping.IsRequired)
            {
                AddOperation(
                    model,
                    "Product attributes",
                    $"{definition.Name} mapping settings",
                    sourceName,
                    $"Control={(AttributeControlType)productMapping.AttributeControlTypeId}; Required={productMapping.IsRequired}",
                    $"Control={AttributeControlType.DropdownList}; Required={mapped.Mapping.IsRequired}",
                    AkeneoDryRunOperationType.Update,
                    "The existing product-attribute mapping metadata would be normalized.",
                    mapped.Mapping.IsRequired);
            }

            var currentValues = (await productAttributeService
                .GetProductAttributeValuesAsync(productMapping.Id)).ToList();
            var combinations = product == null
                ? new List<ProductAttributeCombination>()
                : (await productAttributeService
                    .GetAllProductAttributeCombinationsAsync(product.Id)).ToList();

            var desiredValueIds = new HashSet<int>();
            var desiredStateComplete = true;

            foreach (var item in desiredItems)
            {
                var displayName = item.DisplayName?.Trim() ?? item.AkeneoOptionCode?.Trim();
                if (string.IsNullOrWhiteSpace(displayName))
                    continue;

                var mappingCode =
                    $"product-attribute:{context.ProductKey?.Trim()}:{attributeCode}:{(item.AkeneoOptionCode ?? displayName).Trim()}";

                var mappedValueId = await entityMappingService
                    .GetMappedNopEntityIdByAkeneoCodeAsync(
                        AkeneoEntityType.Option,
                        mappingCode,
                        NopEntityType.ProductAttributeValue);

                ProductAttributeValue value = null;
                if (mappedValueId.HasValue)
                {
                    value = currentValues.FirstOrDefault(candidate =>
                        candidate.Id == mappedValueId.Value);
                }

                value ??= currentValues.FirstOrDefault(candidate =>
                    candidate.AttributeValueTypeId == (int)AttributeValueType.Simple &&
                    string.Equals(candidate.Name, displayName, StringComparison.OrdinalIgnoreCase));

                if (value == null)
                {
                    if (!context.Request.CreateMissingProductAttributeValues)
                    {
                        desiredStateComplete = false;
                        AddReviewOperation(
                            model,
                            "Product attributes",
                            $"{definition.Name}: {displayName}",
                            sourceName,
                            "The value does not exist and the profile does not allow missing product attribute values to be created.",
                            mapped.Mapping.IsRequired,
                            "Value absent",
                            "Value absent");
                        continue;
                    }

                    AddOperation(
                        model,
                        "Product attributes",
                        $"{definition.Name}: {displayName}",
                        sourceName,
                        "Value absent",
                        "Create value",
                        AkeneoDryRunOperationType.Add,
                        "A simple product attribute value would be created.",
                        mapped.Mapping.IsRequired);
                    continue;
                }

                desiredValueIds.Add(value.Id);

                if (!string.Equals(value.Name, displayName, StringComparison.OrdinalIgnoreCase))
                {
                    AddOperation(
                        model,
                        "Product attributes",
                        $"{definition.Name} value",
                        sourceName,
                        value.Name,
                        displayName,
                        AkeneoDryRunOperationType.Update,
                        "The Akeneo-mapped product attribute value would be renamed.",
                        mapped.Mapping.IsRequired);
                }
                else
                {
                    AddOperation(
                        model,
                        "Product attributes",
                        $"{definition.Name}: {displayName}",
                        sourceName,
                        "Value exists",
                        "Value exists",
                        AkeneoDryRunOperationType.NoChange,
                        null,
                        mapped.Mapping.IsRequired);
                }
            }

            if (context.Request.ProductAttributeSyncMode == AkeneoCollectionSyncMode.Merge)
                continue;

            if (!desiredStateComplete)
            {
                model.CoverageNotes.Add(
                    $"Stale product attribute values for '{definition.Name}' are not planned for removal because its desired state is incomplete.");
                continue;
            }

            IEnumerable<ProductAttributeValue> stale;

            if (context.Request.ProductAttributeSyncMode == AkeneoCollectionSyncMode.ReplaceAll)
            {
                stale = currentValues.Where(value =>
                    value.AttributeValueTypeId == (int)AttributeValueType.Simple &&
                    !desiredValueIds.Contains(value.Id));
            }
            else if (context.Request.SyncProfileId.HasValue && product != null)
            {
                var managed = await managedRelationService.GetByProductAsync(
                    context.Request.SyncProfileId.Value,
                    product.Id,
                    AkeneoManagedRelationType.ProductAttributeValue,
                    attributeCode);

                var managedIds = managed.Select(relation => relation.NopRelationEntityId).ToHashSet();
                stale = currentValues.Where(value =>
                    managedIds.Contains(value.Id) &&
                    !desiredValueIds.Contains(value.Id));
            }
            else
            {
                model.CoverageNotes.Add(
                    $"Replace-managed product attribute removals for '{definition.Name}' require a saved sync profile and cannot be planned without one.");
                continue;
            }

            var plannedRemovalIds = new HashSet<int>();

            foreach (var value in stale)
            {
                if (IsValueUsedByCombination(value.Id, combinations))
                {
                    AddOperation(
                        model,
                        "Product attributes",
                        $"{definition.Name}: {value.Name}",
                        sourceName,
                        "Value exists",
                        "Preserved",
                        AkeneoDryRunOperationType.Preserve,
                        "The stale value is used by a product attribute combination, so the real synchronizer preserves it.",
                        mapped.Mapping.IsRequired);
                    continue;
                }

                AddOperation(
                    model,
                    "Product attributes",
                    $"{definition.Name}: {value.Name}",
                    sourceName,
                    "Value exists",
                    "Removed",
                    AkeneoDryRunOperationType.Remove,
                    context.Request.ProductAttributeSyncMode == AkeneoCollectionSyncMode.ReplaceManaged
                        ? "This value is plugin-owned and stale for this mapping."
                        : "Replace-all makes this product attribute match the desired Akeneo values.",
                    mapped.Mapping.IsRequired);

                plannedRemovalIds.Add(value.Id);
            }

            await PlanEmptyMappingRemovalAsync(
                context,
                model,
                mapped,
                definition,
                productMapping,
                currentValues,
                desiredItems.Count > 0,
                plannedRemovalIds);
        }
    }

    private async Task PlanEmptyMappingRemovalAsync(
        AkeneoProductSyncContext context,
        AkeneoProductMappingPreviewModel model,
        AkeneoResolvedMappedValue mapped,
        ProductAttribute definition,
        ProductAttributeMapping productMapping,
        IReadOnlyCollection<ProductAttributeValue> currentValues,
        bool hasDesiredValues,
        ISet<int> plannedRemovalIds)
    {
        if (hasDesiredValues ||
            currentValues.Any(value => !plannedRemovalIds.Contains(value.Id)))
        {
            return;
        }

        if (context.Request.ProductAttributeSyncMode == AkeneoCollectionSyncMode.ReplaceAll)
        {
            AddOperation(
                model,
                "Product attributes",
                $"{definition.Name} mapping",
                GetSourceName(mapped),
                "Mapping exists",
                "Removed",
                AkeneoDryRunOperationType.Remove,
                "Replace-all deletes the product-attribute mapping after its last value is removed.",
                mapped.Mapping.IsRequired);
            return;
        }

        if (!context.Request.SyncProfileId.HasValue)
            return;

        var relation = await managedRelationService.GetByRelationEntityAsync(
            context.Request.SyncProfileId.Value,
            AkeneoManagedRelationType.ProductAttributeMapping,
            productMapping.Id);

        if (relation == null ||
            !string.Equals(
                relation.AkeneoAttributeCode,
                mapped.Mapping.AkeneoAttributeCode,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        AddOperation(
            model,
            "Product attributes",
            $"{definition.Name} mapping",
            GetSourceName(mapped),
            "Plugin-managed mapping exists",
            "Removed",
            AkeneoDryRunOperationType.Remove,
            "Replace-managed deletes the plugin-owned mapping after its last managed value is removed.",
            mapped.Mapping.IsRequired);
    }

    private async Task<HashSet<string>> GetVariantAxisCodesAsync(string familyCode)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(familyCode))
            return result;

        var family = await familyMappingService.GetByFamilyCodeAsync(familyCode);
        if (family == null)
            return result;

        var axes = await familyMappingService.GetAxisMappingsAsync(family.Id);
        foreach (var axis in axes.Where(axis =>
                     !string.IsNullOrWhiteSpace(axis.AkeneoAttributeCode)))
        {
            result.Add(axis.AkeneoAttributeCode.Trim());
        }

        return result;
    }
}
