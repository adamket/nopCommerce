using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Nop.Core.Domain.Catalog;
using Nop.Services.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public class AkeneoProductSyncService(
    IAkeneoProductValueResolver productValueResolver,
    IAkeneoReferenceEntityValueResolver referenceEntityValueResolver,
    IAkeneoValueTransformationService transformationService,
    IAkeneoAttributeMappingService attributeMappingService,
    IAkeneoNopEntityMappingService entityMappingService,
    IAkeneoProductSyncStateService syncStateService,
    IProductService productService,
    IAkeneoProductSyncPipeline syncPipeline)
    : IAkeneoProductSyncService
{
    public async Task<AkeneoProductSyncContext> PrepareAsync(
        AkeneoProductDefinition source,
        AkeneoEntityType sourceEntityType,
        AkeneoProductImportRequest request,
        AkeneoProductImportResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);

        cancellationToken.ThrowIfCancellationRequested();

        var sourceCode = sourceEntityType == AkeneoEntityType.ProductModel
            ? source.Code?.Trim()
            : source.Identifier?.Trim();

        var sourceUuid = sourceEntityType == AkeneoEntityType.Product
            ? source.Uuid?.Trim()
            : null;

        var productKey = sourceUuid ?? sourceCode;

        result.AkeneoProductUuid = sourceUuid;
        result.AkeneoIdentifier = sourceCode;
        result.AkeneoProductKey = productKey;

        if (string.IsNullOrWhiteSpace(productKey))
        {
            result.AddError(
                sourceEntityType == AkeneoEntityType.ProductModel
                    ? "Akeneo product model does not contain a code."
                    : "Akeneo product does not contain a UUID or identifier.");
        }

        var mappings = await attributeMappingService
            .GetEffectiveMappingsAsync(source.Family);

        var mappedValues = await ResolveMappedValuesAsync(
            source,
            mappings,
            request,
            result,
            cancellationToken);

        AppendUnmappedAttributeValues(
            source,
            mappings,
            request,
            result,
            mappedValues);

        var sku = ResolveSku(
            source,
            sourceEntityType,
            mappedValues);

        result.Sku = sku;

        Product existingProduct = null;
        AkeneoProductSyncState existingState = null;

        if (result.Success)
        {
            (existingProduct, existingState) = await ResolveNopProductAsync(
                sourceEntityType,
                sourceCode,
                sourceUuid,
                sku,
                !string.IsNullOrWhiteSpace(source.Parent),
                request,
                result);
        }

        return new AkeneoProductSyncContext
        {
            Source = source,
            SourceEntityType = sourceEntityType,
            Request = request,
            Result = result,
            MappedValues = mappedValues.ToList(),
            SourceCode = sourceCode,
            SourceUuid = sourceUuid,
            ProductKey = productKey,
            Sku = sku,
            ExistingProduct = existingProduct,
            ExistingSyncState = existingState
        };
    }

    public async Task<Product> SynchronizeAsync(
        AkeneoProductSyncContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Result.Success)
        {
            context.Result.ActionType = SyncItemActionType.Failed;
            return null;
        }

        await syncPipeline.SynchronizeAsync(
            context,
            cancellationToken);

        if (!context.Result.Success)
        {
            context.Result.ActionType = SyncItemActionType.Failed;
            return null;
        }

        if (context.Product != null)
        {
            await entityMappingService.UpsertAkeneoNopEntityMappingAsync(
                context.SourceEntityType,
                context.SourceCode,
                context.SourceUuid,
                NopEntityType.Product,
                context.Product.Id);

            context.Result.NopProductId = context.Product.Id;
            context.Result.DestinationKind = context.Product.ParentGroupedProductId > 0
                ? AkeneoProductDestinationKind.GroupedChildProduct
                : AkeneoProductDestinationKind.NopProduct;
        }

        if (context.ProductCreated)
        {
            context.Result.ActionType = SyncItemActionType.Created;
        }
        else if (context.HasChanges)
        {
            context.Result.ActionType = SyncItemActionType.Updated;
        }
        else if (context.Result.ActionType != SyncItemActionType.Failed)
        {
            context.Result.ActionType = SyncItemActionType.Skipped;

            if (!context.Result.Messages.Any())
            {
                context.Result.AddMessage(
                    "Product already matches the Akeneo desired state.");
            }
        }

        return context.Product;
    }

    private async Task<IList<AkeneoResolvedMappedValue>> ResolveMappedValuesAsync(
        AkeneoProductDefinition source,
        IList<AkeneoAttributeMapping> mappings,
        AkeneoProductImportRequest request,
        AkeneoProductImportResult result,
        CancellationToken cancellationToken)
    {
        var resolved = new List<AkeneoResolvedMappedValue>();

        foreach (var mapping in mappings)
        {
            var targetType = (NopTargetType)mapping.NopTargetTypeId;

            if (targetType == NopTargetType.Ignore)
                continue;

            var locale = !string.IsNullOrWhiteSpace(mapping.Locale)
                ? mapping.Locale
                : request.Locale;
            var channel = !string.IsNullOrWhiteSpace(mapping.Channel)
                ? mapping.Channel
                : request.Channel;

            AkeneoResolvedProductValue value;
            bool resolvedSuccessfully;

            if (IsReferenceEntityMapping(mapping) &&
                !string.IsNullOrWhiteSpace(mapping.AkeneoReferenceEntityAttributeCode))
            {
                value = await referenceEntityValueResolver.ResolveAsync(
                    source,
                    mapping,
                    locale,
                    channel,
                    request.Currency,
                    cancellationToken);

                resolvedSuccessfully = value != null;
            }
            else
            {
                resolvedSuccessfully = productValueResolver.TryGetValue(
                    source,
                    mapping.AkeneoAttributeCode,
                    out value,
                    locale,
                    channel,
                    request.Currency);
            }

            if (resolvedSuccessfully && value != null &&
                !string.IsNullOrWhiteSpace(mapping.TransformRuleJson))
            {
                var transformed = transformationService.Transform(value, mapping);

                if (!transformed.Success)
                {
                    var message =
                        $"Transform failed for Akeneo source '{AkeneoMappingHelper.GetSourceDisplayName(mapping)}': {transformed.Error}";

                    if (mapping.IsRequired)
                        result.AddError(message);
                    else
                        result.AddWarning(message);

                    resolvedSuccessfully = false;
                    value = null;
                }
                else
                {
                    value = transformed.Value;
                }
            }

            var hasDisplayValue =
                !string.IsNullOrWhiteSpace(value?.DisplayValue) ||
                value?.DisplayValues is { Count: > 0 };

            var hasValue = resolvedSuccessfully && hasDisplayValue;

            if (!hasValue && mapping.IsRequired)
            {
                result.AddError(
                    $"Required Akeneo source is missing a value: {AkeneoMappingHelper.GetSourceDisplayName(mapping)}");
            }

            // Missing optional mappings are retained so destination sections can
            // apply PreserveExisting or ClearExisting consistently.
            resolved.Add(new AkeneoResolvedMappedValue
            {
                Mapping = mapping,
                HasValue = hasValue,
                Value = value
            });
        }

        return resolved;
    }

    private static bool IsReferenceEntityMapping(
        AkeneoAttributeMapping mapping)
    {
        return mapping.AkeneoAttributeTypeId == (int)AkeneoAttributeType.ReferenceEntity ||
               mapping.AkeneoAttributeTypeId == (int)AkeneoAttributeType.ReferenceEntityCollection;
    }

    private void AppendUnmappedAttributeValues(
        AkeneoProductDefinition source,
        IList<AkeneoAttributeMapping> effectiveMappings,
        AkeneoProductImportRequest request,
        AkeneoProductImportResult result,
        IList<AkeneoResolvedMappedValue> mappedValues)
    {
        if (request is not AkeneoProductBatchImportRequest batchRequest ||
            batchRequest.UnmappedAttributeBehavior == UnmappedAkeneoAttributeBehavior.Ignore ||
            source.Values.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            return;
        }

        var handledCodes = effectiveMappings
            .Where(mapping => !string.IsNullOrWhiteSpace(mapping.AkeneoAttributeCode))
            .Select(mapping => mapping.AkeneoAttributeCode.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var unmappedCodes = source.Values
            .EnumerateObject()
            .Select(property => property.Name)
            .Where(code => !handledCodes.Contains(code))
            .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (unmappedCodes.Count == 0)
            return;

        if (batchRequest.UnmappedAttributeBehavior == UnmappedAkeneoAttributeBehavior.Log)
        {
            result.AddWarning(
                "Unmapped Akeneo attributes: " + string.Join(", ", unmappedCodes.Take(25)) +
                (unmappedCodes.Count > 25 ? $" (+{unmappedCodes.Count - 25} more)" : string.Empty));
            return;
        }

        if (batchRequest.UnmappedAttributeBehavior ==
            UnmappedAkeneoAttributeBehavior.ImportAsSpecificationAttribute)
        {
            result.AddWarning(
                "Import-as-specification requires explicit destination attribute provisioning. " +
                "The unmapped attributes were preserved but not imported: " +
                string.Join(", ", unmappedCodes.Take(25)));
            return;
        }

        foreach (var code in unmappedCodes)
        {
            if (!productValueResolver.TryGetValue(
                    source,
                    code,
                    out var value,
                    request.Locale,
                    request.Channel,
                    request.Currency))
            {
                continue;
            }

            var hasValue =
                !string.IsNullOrWhiteSpace(value?.DisplayValue) ||
                value?.DisplayValues is { Count: > 0 };

            if (!hasValue)
                continue;

            mappedValues.Add(new AkeneoResolvedMappedValue
            {
                Mapping = new AkeneoAttributeMapping
                {
                    AkeneoFamilyCode = source.Family,
                    AkeneoAttributeCode = code,
                    NopTargetTypeId = (int)NopTargetType.CustomProperty,
                    NopTargetKey = $"Apt.Akeneo.CustomProperty.{code}"
                },
                HasValue = true,
                Value = value
            });
        }
    }

    private static string ResolveSku(
        AkeneoProductDefinition source,
        AkeneoEntityType sourceEntityType,
        IEnumerable<AkeneoResolvedMappedValue> mappedValues)
    {
        var mappedSku = mappedValues
            .FirstOrDefault(value =>
                value.HasValue &&
                value.TargetType == NopTargetType.ProductField &&
                string.Equals(
                    value.Mapping.NopTargetKey,
                    "Sku",
                    StringComparison.OrdinalIgnoreCase))
            ?.DisplayValue;

        if (!string.IsNullOrWhiteSpace(mappedSku))
            return mappedSku.Trim();

        return sourceEntityType == AkeneoEntityType.ProductModel
            ? source.Code?.Trim()
            : source.Identifier?.Trim();
    }

    private async Task<(Product Product, AkeneoProductSyncState State)> ResolveNopProductAsync(
        AkeneoEntityType sourceEntityType,
        string sourceCode,
        string sourceUuid,
        string sku,
        bool isVariantLeaf,
        AkeneoProductImportRequest request,
        AkeneoProductImportResult result)
    {
        AkeneoProductSyncState state = null;

        if (sourceEntityType == AkeneoEntityType.Product &&
            request.SyncProfileId.HasValue)
        {
            state = await syncStateService.GetBySourceAsync(
                request.SyncProfileId.Value,
                sourceEntityType,
                sourceCode,
                sourceUuid);

            if (state != null)
            {
                var destinationKind =
                    (AkeneoProductDestinationKind)state.DestinationKindId;

                // A combination is not a child nop product. Do not return its
                // parent as the leaf product, but continue through legacy mapping
                // and SKU fallback so a previously hidden child can be restored if
                // the representation changes back to grouped/associated/standalone.
                if (destinationKind != AkeneoProductDestinationKind.ProductAttributeCombination)
                {
                    var stateProduct = await GetMappedProductOrWarnAsync(
                        state.NopProductId,
                        $"profile state for Akeneo product {sourceUuid ?? sourceCode}",
                        result);

                    if (stateProduct != null)
                        return (stateProduct, state);
                }
            }
        }

        if (sourceEntityType == AkeneoEntityType.Product &&
            !string.IsNullOrWhiteSpace(sourceUuid))
        {
            var mappedId = await entityMappingService
                .GetMappedNopEntityIdByAkeneoUuidAsync(
                    sourceEntityType,
                    sourceUuid,
                    NopEntityType.Product);

            var product = await GetMappedProductOrWarnAsync(
                mappedId,
                $"Akeneo UUID {sourceUuid}",
                result);

            if (product != null)
            {
                if (isVariantLeaf &&
                    !string.IsNullOrWhiteSpace(sku) &&
                    !string.Equals(product.Sku, sku, StringComparison.OrdinalIgnoreCase))
                {
                    result.AddWarning(
                        $"Ignored legacy UUID mapping to product ID {product.Id} because it does not match variant SKU '{sku}'. The profile sync-state binding will replace it.");
                }
                else
                {
                    return (product, state);
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(sourceCode))
        {
            var mappedId = await entityMappingService
                .GetMappedNopEntityIdByAkeneoCodeAsync(
                    sourceEntityType,
                    sourceCode,
                    NopEntityType.Product);

            var product = await GetMappedProductOrWarnAsync(
                mappedId,
                $"Akeneo {sourceEntityType} code {sourceCode}",
                result);

            if (product != null)
            {
                if (isVariantLeaf &&
                    !string.IsNullOrWhiteSpace(sku) &&
                    !string.Equals(product.Sku, sku, StringComparison.OrdinalIgnoreCase))
                {
                    result.AddWarning(
                        $"Ignored legacy code mapping to product ID {product.Id} because it does not match variant SKU '{sku}'.");
                }
                else
                {
                    return (product, state);
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(sku))
        {
            var product = await productService.GetProductBySkuAsync(sku);

            if (product != null)
            {
                result.AddMessage(
                    $"Matched existing nopCommerce product by SKU '{sku}'.");
            }

            return (product, state);
        }

        return (null, state);
    }

    private async Task<Product> GetMappedProductOrWarnAsync(
        int? mappedProductId,
        string mappingDescription,
        AkeneoProductImportResult result)
    {
        if (!mappedProductId.HasValue || mappedProductId.Value <= 0)
            return null;

        var product = await productService.GetProductByIdAsync(mappedProductId.Value);

        if (product != null)
            return product;

        result.AddWarning(
            $"A mapping exists for {mappingDescription}, but " +
            $"nopCommerce product ID {mappedProductId.Value} was not found.");

        return null;
    }
}
