using System.Xml.Linq;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services.Planning;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Nop.Core.Domain.Catalog;
using Nop.Services.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public partial class AkeneoProductAttributeSynchronizer
    : IAkeneoProductSectionSynchronizer,
      IAkeneoSectionDryRunPlanProvider
{
    private readonly IProductAttributeService _productAttributeService;
    private readonly IAkeneoNopEntityMappingService _entityMappingService;
    private readonly IAkeneoManagedRelationService _managedRelationService;
    private readonly IAkeneoFamilyMappingService _familyMappingService;

    public AkeneoProductAttributeSynchronizer(
        IProductAttributeService productAttributeService,
        IAkeneoNopEntityMappingService entityMappingService,
        IAkeneoManagedRelationService managedRelationService,
        IAkeneoFamilyMappingService familyMappingService)
    {
        _productAttributeService = productAttributeService;
        _entityMappingService = entityMappingService;
        _managedRelationService = managedRelationService;
        _familyMappingService = familyMappingService;
    }

    public int Order => 500;

    public async Task SynchronizeAsync(
        AkeneoProductSyncContext context,
        CancellationToken cancellationToken = default)
    {
        var plan = await BuildPlanAsync(context, cancellationToken);
        await plan.ExecuteAsync(context, cancellationToken);
    }

    private async Task<AkeneoExecutableSectionPlan> BuildPlanAsync(
        AkeneoProductSyncContext context,
        CancellationToken cancellationToken)
    {
        var plan = new AkeneoExecutableSectionPlan();

        if (context.Request.ProductAttributeSyncMode ==
            AkeneoCollectionSyncMode.Disabled)
        {
            plan.CoverageNotes.Add(
                "Product-attribute synchronization is disabled by the saved sync profile.");
            return plan;
        }

        var mappings = context.GetMappings(NopTargetType.ProductAttribute).ToList();
        if (!mappings.Any())
            return plan;

        var product = context.Product ?? context.ExistingProduct;
        var definitions = await _productAttributeService.GetAllProductAttributesAsync();
        var productMappings = product == null
            ? new List<ProductAttributeMapping>()
            : (await _productAttributeService
                .GetProductAttributeMappingsByProductIdAsync(product.Id)).ToList();
        var combinations = product == null
            ? new List<ProductAttributeCombination>()
            : (await _productAttributeService
                .GetAllProductAttributeCombinationsAsync(product.Id)).ToList();
        var variantAxisCodes = await GetVariantAxisCodesAsync(
            context.MappingFamilyCode,
            context.MappingFamilyVariantCode);
        var nextMappingDisplayOrder = productMappings.Count + 1;

        foreach (var mapped in mappings)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var attributeCode = mapped.Mapping.AkeneoAttributeCode?.Trim() ?? string.Empty;
            var sourceName = AkeneoMappingHelper.GetSourceDisplayName(mapped.Mapping);
            var productAttributeId = mapped.Mapping.NopTargetEntityId ?? 0;
            var definition = definitions.FirstOrDefault(item =>
                item.Id == productAttributeId);

            if (variantAxisCodes.Contains(attributeCode))
            {
                var message =
                    $"Skipped generic product-attribute synchronization for variant axis '{mapped.Mapping.AkeneoAttributeCode}'.";
                plan.Messages.Add(message);
                plan.AddOperation(
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

            if (productAttributeId <= 0 || definition == null)
            {
                plan.AddReview(
                    "Product attributes",
                    productAttributeId > 0
                        ? $"Product attribute #{productAttributeId}"
                        : "Missing target",
                    sourceName,
                    $"Product attribute mapping has no valid nopCommerce product attribute. Akeneo attribute: {mapped.Mapping.AkeneoAttributeCode}",
                    mapped.Mapping.IsRequired);
                continue;
            }

            var desiredItems = mapped.HasValue
                ? AkeneoSyncValueHelper.GetOptionItems(mapped)
                : Array.Empty<AkeneoResolvedOptionItem>();
            var state = new ProductAttributeMappingState
            {
                Mapping = productMappings.FirstOrDefault(item =>
                    item.ProductAttributeId == productAttributeId)
            };
            var mappingWillBeCreated = state.Mapping == null && desiredItems.Count > 0;

            if (mappingWillBeCreated)
            {
                var displayOrder = nextMappingDisplayOrder++;

                plan.AddOperation(
                    "Product attributes",
                    $"{definition.Name} mapping",
                    sourceName,
                    "Not configured on product",
                    "Create dropdown mapping",
                    AkeneoDryRunOperationType.Add,
                    "A ProductAttributeMapping would be created before its values are synchronized.",
                    mapped.Mapping.IsRequired,
                    async _ =>
                    {
                        if (product == null)
                            return false;

                        state.Mapping = new ProductAttributeMapping
                        {
                            ProductId = product.Id,
                            ProductAttributeId = productAttributeId,
                            AttributeControlTypeId = (int)AttributeControlType.DropdownList,
                            IsRequired = mapped.Mapping.IsRequired,
                            DisplayOrder = displayOrder
                        };

                        await _productAttributeService
                            .InsertProductAttributeMappingAsync(state.Mapping);

                        if (context.Request.SyncProfileId.HasValue)
                        {
                            await _managedRelationService.UpsertAsync(
                                context.Request.SyncProfileId.Value,
                                context.Request.SyncRunRecordId,
                                product.Id,
                                AkeneoManagedRelationType.ProductAttributeMapping,
                                state.Mapping.Id,
                                mapped.Mapping.AkeneoAttributeCode);
                        }

                        return true;
                    });
            }

            if (state.Mapping == null && !mappingWillBeCreated)
                continue;

            if (state.Mapping != null &&
                (state.Mapping.AttributeControlTypeId !=
                     (int)AttributeControlType.DropdownList ||
                 state.Mapping.IsRequired != mapped.Mapping.IsRequired))
            {
                var currentSettings =
                    $"Control={(AttributeControlType)state.Mapping.AttributeControlTypeId}; Required={state.Mapping.IsRequired}";
                var proposedSettings =
                    $"Control={AttributeControlType.DropdownList}; Required={mapped.Mapping.IsRequired}";

                plan.AddOperation(
                    "Product attributes",
                    $"{definition.Name} mapping settings",
                    sourceName,
                    currentSettings,
                    proposedSettings,
                    AkeneoDryRunOperationType.Update,
                    "The existing product-attribute mapping metadata would be normalized.",
                    mapped.Mapping.IsRequired,
                    async _ =>
                    {
                        state.Mapping.AttributeControlTypeId =
                            (int)AttributeControlType.DropdownList;
                        state.Mapping.IsRequired = mapped.Mapping.IsRequired;
                        await _productAttributeService
                            .UpdateProductAttributeMappingAsync(state.Mapping);
                        return true;
                    });
            }

            var currentValues = state.Mapping?.Id > 0
                ? (await _productAttributeService
                    .GetProductAttributeValuesAsync(state.Mapping.Id)).ToList()
                : new List<ProductAttributeValue>();
            var desiredValueIds = new HashSet<int>();
            var desiredStateComplete = true;
            var nextValueDisplayOrder = currentValues.Count + 1;
            var plannedNewValuesByName =
                new Dictionary<string, ProductAttributeValueState>(
                    StringComparer.OrdinalIgnoreCase);

            foreach (var item in desiredItems)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var displayName = item.DisplayName?.Trim() ??
                                  item.AkeneoOptionCode?.Trim();
                if (string.IsNullOrWhiteSpace(displayName))
                    continue;

                var mappingCode = BuildMappingCode(
                    context.ProductKey,
                    attributeCode,
                    item.AkeneoOptionCode ?? displayName);
                ProductAttributeValue value = null;
                var pluginOwned = false;

                if (state.Mapping?.Id > 0)
                {
                    var mappedValueId = await _entityMappingService
                        .GetMappedNopEntityIdByAkeneoCodeAsync(
                            AkeneoEntityType.Option,
                            mappingCode,
                            NopEntityType.ProductAttributeValue);

                    if (mappedValueId.HasValue)
                    {
                        var mappedValue = await _productAttributeService
                            .GetProductAttributeValueByIdAsync(mappedValueId.Value);

                        if (mappedValue != null &&
                            mappedValue.ProductAttributeMappingId == state.Mapping.Id)
                        {
                            value = mappedValue;
                            pluginOwned = true;
                        }
                    }

                    value ??= currentValues.FirstOrDefault(candidate =>
                        candidate.AttributeValueTypeId == (int)AttributeValueType.Simple &&
                        string.Equals(
                            candidate.Name,
                            displayName,
                            StringComparison.OrdinalIgnoreCase));
                }

                if (value == null)
                {
                    if (plannedNewValuesByName.ContainsKey(displayName))
                    {
                        plan.AddOperation(
                            "Product attributes",
                            $"{definition.Name}: {displayName}",
                            sourceName,
                            "Value absent",
                            "Created by an earlier option in this plan",
                            AkeneoDryRunOperationType.NoChange,
                            "A prior Akeneo option in this mapping already plans creation of the same nopCommerce display value.",
                            mapped.Mapping.IsRequired);
                        continue;
                    }

                    if (!context.Request.CreateMissingProductAttributeValues)
                    {
                        desiredStateComplete = false;
                        plan.AddReview(
                            "Product attributes",
                            $"{definition.Name}: {displayName}",
                            sourceName,
                            "The value does not exist and the profile does not allow missing product attribute values to be created.",
                            mapped.Mapping.IsRequired,
                            "Value absent",
                            "Value absent");
                        continue;
                    }

                    var valueState = new ProductAttributeValueState();
                    plannedNewValuesByName[displayName] = valueState;
                    var displayOrder = nextValueDisplayOrder++;

                    plan.AddOperation(
                        "Product attributes",
                        $"{definition.Name}: {displayName}",
                        sourceName,
                        "Value absent",
                        "Create value",
                        AkeneoDryRunOperationType.Add,
                        "A simple product attribute value would be created.",
                        mapped.Mapping.IsRequired,
                        async _ =>
                        {
                            if (product == null || state.Mapping == null)
                                return false;

                            valueState.Value = new ProductAttributeValue
                            {
                                ProductAttributeMappingId = state.Mapping.Id,
                                AttributeValueTypeId = (int)AttributeValueType.Simple,
                                Name = displayName,
                                DisplayOrder = displayOrder
                            };

                            await _productAttributeService
                                .InsertProductAttributeValueAsync(valueState.Value);
                            await _entityMappingService
                                .UpsertAkeneoNopEntityMappingAsync(
                                    AkeneoEntityType.Option,
                                    mappingCode,
                                    null,
                                    NopEntityType.ProductAttributeValue,
                                    valueState.Value.Id);

                            if (context.Request.SyncProfileId.HasValue)
                            {
                                await _managedRelationService.UpsertAsync(
                                    context.Request.SyncProfileId.Value,
                                    context.Request.SyncRunRecordId,
                                    product.Id,
                                    AkeneoManagedRelationType.ProductAttributeValue,
                                    valueState.Value.Id,
                                    mapped.Mapping.AkeneoAttributeCode,
                                    item.AkeneoOptionCode ?? displayName);
                            }

                            return true;
                        });

                    continue;
                }

                desiredValueIds.Add(value.Id);

                if (pluginOwned &&
                    !string.Equals(value.Name, displayName, StringComparison.OrdinalIgnoreCase))
                {
                    var mappedValue = value;
                    plan.AddOperation(
                        "Product attributes",
                        $"{definition.Name} value",
                        sourceName,
                        mappedValue.Name,
                        displayName,
                        AkeneoDryRunOperationType.Update,
                        "The Akeneo-mapped product attribute value would be renamed.",
                        mapped.Mapping.IsRequired,
                        async _ =>
                        {
                            mappedValue.Name = displayName;
                            await _productAttributeService
                                .UpdateProductAttributeValueAsync(mappedValue);

                            if (context.Request.SyncProfileId.HasValue && product != null)
                            {
                                await _managedRelationService.UpsertAsync(
                                    context.Request.SyncProfileId.Value,
                                    context.Request.SyncRunRecordId,
                                    product.Id,
                                    AkeneoManagedRelationType.ProductAttributeValue,
                                    mappedValue.Id,
                                    mapped.Mapping.AkeneoAttributeCode,
                                    item.AkeneoOptionCode ?? displayName);
                            }

                            return true;
                        });
                }
                else
                {
                    var existingValue = value;
                    plan.AddOperation(
                        "Product attributes",
                        $"{definition.Name}: {displayName}",
                        sourceName,
                        "Value exists",
                        "Value exists",
                        AkeneoDryRunOperationType.NoChange,
                        isRequired: mapped.Mapping.IsRequired,
                        executeAsync: pluginOwned &&
                                      context.Request.SyncProfileId.HasValue &&
                                      product != null
                            ? async _ =>
                            {
                                await _managedRelationService.UpsertAsync(
                                    context.Request.SyncProfileId.Value,
                                    context.Request.SyncRunRecordId,
                                    product.Id,
                                    AkeneoManagedRelationType.ProductAttributeValue,
                                    existingValue.Id,
                                    mapped.Mapping.AkeneoAttributeCode,
                                    item.AkeneoOptionCode ?? displayName);
                                return false;
                            }
                            : null);
                }
            }

            if (context.Request.ProductAttributeSyncMode ==
                AkeneoCollectionSyncMode.Merge)
            {
                continue;
            }

            if (!desiredStateComplete)
            {
                var warning =
                    $"Product attribute removal for '{mapped.Mapping.AkeneoAttributeCode}' was skipped because the desired state was incomplete.";
                plan.Warnings.Add(warning);
                plan.CoverageNotes.Add(
                    $"Stale product attribute values for '{definition.Name}' are not planned for removal because its desired state is incomplete.");
                continue;
            }

            if (state.Mapping == null)
                continue;

            var plannedRemovalIds = new HashSet<int>();

            if (context.Request.ProductAttributeSyncMode ==
                AkeneoCollectionSyncMode.ReplaceAll)
            {
                foreach (var staleValue in currentValues.Where(value =>
                             value.AttributeValueTypeId == (int)AttributeValueType.Simple &&
                             !desiredValueIds.Contains(value.Id)))
                {
                    if (IsValueUsedByCombination(staleValue.Id, combinations))
                    {
                        var warning =
                            $"Stale product attribute value '{staleValue.Name}' is used by a product attribute combination and was preserved.";
                        plan.Warnings.Add(warning);
                        plan.AddOperation(
                            "Product attributes",
                            $"{definition.Name}: {staleValue.Name}",
                            sourceName,
                            "Value exists",
                            "Preserved",
                            AkeneoDryRunOperationType.Preserve,
                            "The stale value is used by a product attribute combination, so it is preserved.",
                            mapped.Mapping.IsRequired);
                        continue;
                    }

                    plannedRemovalIds.Add(staleValue.Id);
                    plan.AddOperation(
                        "Product attributes",
                        $"{definition.Name}: {staleValue.Name}",
                        sourceName,
                        "Value exists",
                        "Removed",
                        AkeneoDryRunOperationType.Remove,
                        "Replace-all makes this product attribute match the desired Akeneo values.",
                        mapped.Mapping.IsRequired,
                        async _ =>
                        {
                            await _productAttributeService
                                .DeleteProductAttributeValueAsync(staleValue);
                            return true;
                        });
                }
            }
            else if (context.Request.SyncProfileId.HasValue && product != null)
            {
                var managedValues = await _managedRelationService.GetByProductAsync(
                    context.Request.SyncProfileId.Value,
                    product.Id,
                    AkeneoManagedRelationType.ProductAttributeValue,
                    mapped.Mapping.AkeneoAttributeCode);

                foreach (var relation in managedValues)
                {
                    if (desiredValueIds.Contains(relation.NopRelationEntityId))
                        continue;

                    var staleValue = currentValues.FirstOrDefault(item =>
                        item.Id == relation.NopRelationEntityId);

                    if (staleValue == null)
                    {
                        plan.AddInternalOperation(async _ =>
                        {
                            await _managedRelationService.DeleteAsync(relation);
                            return false;
                        });
                        continue;
                    }

                    if (IsValueUsedByCombination(staleValue.Id, combinations))
                    {
                        var warning =
                            $"Stale product attribute value '{staleValue.Name}' is used by a product attribute combination and was preserved.";
                        plan.Warnings.Add(warning);
                        plan.AddOperation(
                            "Product attributes",
                            $"{definition.Name}: {staleValue.Name}",
                            sourceName,
                            "Value exists",
                            "Preserved",
                            AkeneoDryRunOperationType.Preserve,
                            "The stale value is used by a product attribute combination, so it is preserved.",
                            mapped.Mapping.IsRequired);
                        continue;
                    }

                    plannedRemovalIds.Add(staleValue.Id);
                    plan.AddOperation(
                        "Product attributes",
                        $"{definition.Name}: {staleValue.Name}",
                        sourceName,
                        "Value exists",
                        "Removed",
                        AkeneoDryRunOperationType.Remove,
                        "This value is plugin-owned and stale for this mapping.",
                        mapped.Mapping.IsRequired,
                        async _ =>
                        {
                            await _productAttributeService
                                .DeleteProductAttributeValueAsync(staleValue);
                            await _managedRelationService.DeleteAsync(relation);
                            return true;
                        });
                }
            }
            else
            {
                plan.CoverageNotes.Add(
                    $"Replace-managed product attribute removals for '{definition.Name}' require a saved sync profile and cannot be planned without one.");
                continue;
            }

            if (desiredItems.Count > 0 ||
                currentValues.Any(value => !plannedRemovalIds.Contains(value.Id)))
            {
                continue;
            }

            if (context.Request.ProductAttributeSyncMode ==
                AkeneoCollectionSyncMode.ReplaceAll)
            {
                plan.AddOperation(
                    "Product attributes",
                    $"{definition.Name} mapping",
                    sourceName,
                    "Mapping exists",
                    "Removed",
                    AkeneoDryRunOperationType.Remove,
                    "Replace-all deletes the product-attribute mapping after its last value is removed.",
                    mapped.Mapping.IsRequired,
                    async _ =>
                    {
                        await _productAttributeService
                            .DeleteProductAttributeMappingAsync(state.Mapping);
                        return true;
                    });
                continue;
            }

            if (!context.Request.SyncProfileId.HasValue)
                continue;

            var mappingRelation = await _managedRelationService
                .GetByRelationEntityAsync(
                    context.Request.SyncProfileId.Value,
                    AkeneoManagedRelationType.ProductAttributeMapping,
                    state.Mapping.Id);

            if (mappingRelation == null ||
                !string.Equals(
                    mappingRelation.AkeneoAttributeCode,
                    mapped.Mapping.AkeneoAttributeCode,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            plan.AddOperation(
                "Product attributes",
                $"{definition.Name} mapping",
                sourceName,
                "Plugin-managed mapping exists",
                "Removed",
                AkeneoDryRunOperationType.Remove,
                "Replace-managed deletes the plugin-owned mapping after its last managed value is removed.",
                mapped.Mapping.IsRequired,
                async _ =>
                {
                    await _productAttributeService
                        .DeleteProductAttributeMappingAsync(state.Mapping);
                    await _managedRelationService.DeleteAsync(mappingRelation);
                    return true;
                });
        }

        return plan;
    }

    private async Task<HashSet<string>> GetVariantAxisCodesAsync(
        string familyCode,
        string familyVariantCode)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(familyCode))
            return result;

        var family = await _familyMappingService.GetEffectiveMappingAsync(
            familyCode,
            familyVariantCode);
        if (family is not { Enabled: true })
            return result;

        var axes = await _familyMappingService.GetAxisMappingsAsync(family.Id);

        foreach (var axis in axes.Where(axis =>
                     !string.IsNullOrWhiteSpace(axis.AkeneoAttributeCode)))
        {
            result.Add(axis.AkeneoAttributeCode.Trim());
        }

        return result;
    }

    private static bool IsValueUsedByCombination(
        int productAttributeValueId,
        IEnumerable<ProductAttributeCombination> combinations)
    {
        foreach (var combination in combinations)
        {
            if (string.IsNullOrWhiteSpace(combination.AttributesXml))
                continue;

            try
            {
                var document = XDocument.Parse(combination.AttributesXml);
                var used = document
                    .Descendants("Value")
                    .Any(element => int.TryParse(element.Value, out var valueId) &&
                                    valueId == productAttributeValueId);

                if (used)
                    return true;
            }
            catch
            {
                // Preserve on malformed XML rather than risk deleting a value
                // that may still be referenced.
                return true;
            }
        }

        return false;
    }

    private static string BuildMappingCode(
        string productKey,
        string attributeCode,
        string optionCode) =>
        $"product-attribute:{productKey?.Trim()}:{attributeCode?.Trim()}:{optionCode?.Trim()}";

    private sealed class ProductAttributeMappingState
    {
        public ProductAttributeMapping Mapping { get; set; }
    }

    private sealed class ProductAttributeValueState
    {
        public ProductAttributeValue Value { get; set; }
    }
}
