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
    IAkeneoValueTemplateRenderer valueTemplateRenderer,
    IAkeneoAttributeMappingService attributeMappingService,
    IAkeneoAssetMappingService assetMappingService,
    IAkeneoNopEntityMappingService entityMappingService,
    IAkeneoProductSyncStateService syncStateService,
    IProductService productService,
    IAkeneoProductSyncPipeline syncPipeline)
    : IAkeneoProductSyncService
{
    public Task<AkeneoProductSyncContext> PrepareAsync(
        AkeneoProductDefinition source,
        AkeneoEntityType sourceEntityType,
        AkeneoProductImportRequest request,
        AkeneoProductImportResult result,
        CancellationToken cancellationToken = default)
    {
        return PrepareAsync(
            source,
            sourceEntityType,
            request,
            result,
            source?.Family,
            AkeneoAttributeMappingScopeHelper.ResolveDefaultCurrentScope(
                source,
                sourceEntityType),
            cancellationToken);
    }

    public Task<AkeneoProductSyncContext> PrepareAsync(
        AkeneoProductDefinition source,
        AkeneoEntityType sourceEntityType,
        AkeneoProductImportRequest request,
        AkeneoProductImportResult result,
        string mappingFamilyCode,
        CancellationToken cancellationToken = default)
    {
        return PrepareAsync(
            source,
            sourceEntityType,
            request,
            result,
            mappingFamilyCode,
            AkeneoAttributeMappingScopeHelper.ResolveDefaultCurrentScope(
                source,
                sourceEntityType),
            cancellationToken);
    }

    public async Task<AkeneoProductSyncContext> PrepareAsync(
        AkeneoProductDefinition source,
        AkeneoEntityType sourceEntityType,
        AkeneoProductImportRequest request,
        AkeneoProductImportResult result,
        string mappingFamilyCode,
        AkeneoAttributeMappingEntityScope mappingEntityScope,
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

        mappingFamilyCode = !string.IsNullOrWhiteSpace(mappingFamilyCode)
            ? mappingFamilyCode.Trim()
            : source.Family?.Trim();

        mappingEntityScope =
            AkeneoAttributeMappingScopeHelper.NormalizeCurrentScope(
                mappingEntityScope,
                source,
                sourceEntityType);

        var effectiveMappings = await attributeMappingService
            .GetEffectiveMappingsAsync(mappingFamilyCode);

        var applicableMappings = effectiveMappings
            .Where(mapping =>
                AkeneoAttributeMappingScopeHelper.AppliesTo(
                    mapping,
                    mappingEntityScope))
            .ToList();

        var handledAssetAttributeCodes = (await assetMappingService
                .GetEffectiveMappingsAsync(mappingFamilyCode))
            .Where(mapping => mapping.Enabled)
            .Where(mapping => !string.IsNullOrWhiteSpace(
                mapping.SourceAttributeCode))
            .Select(mapping => mapping.SourceAttributeCode.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var fallbackSourcesByMappingId =
            (await attributeMappingService.GetAllFallbackSourcesAsync())
            .Where(fallback => effectiveMappings.Any(mapping =>
                mapping.Id == fallback.AttributeMappingId))
            .GroupBy(fallback => fallback.AttributeMappingId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<AkeneoAttributeMappingFallbackSource>)group
                    .OrderBy(fallback => fallback.DisplayOrder)
                    .ThenBy(fallback => fallback.Id)
                    .ToList());

        var mappedValues = await ResolveMappedValuesAsync(
            source,
            applicableMappings,
            fallbackSourcesByMappingId,
            mappingFamilyCode,
            request,
            result,
            cancellationToken);

        // A configured mapping that does not apply to this destination role is
        // still considered handled. It must not be logged or imported as an
        // "unmapped" custom property on the wrong product role.
        AppendUnmappedAttributeValues(
            source,
            effectiveMappings,
            fallbackSourcesByMappingId,
            handledAssetAttributeCodes,
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
            MappingFamilyCode = mappingFamilyCode,
            MappingEntityScope = mappingEntityScope,
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
        IReadOnlyDictionary<int, IReadOnlyList<AkeneoAttributeMappingFallbackSource>> fallbackSourcesByMappingId,
        string mappingFamilyCode,
        AkeneoProductImportRequest request,
        AkeneoProductImportResult result,
        CancellationToken cancellationToken)
    {
        var resolved = new List<AkeneoResolvedMappedValue>();

        // Ordinary mappings are resolved first so templates can use the
        // effective SKU produced by a normal ProductField/Sku mapping.
        foreach (var mapping in mappings.Where(mapping =>
                     mapping.ValueModeId !=
                     (int)AkeneoAttributeMappingValueMode.Template))
        {
            resolved.Add(await ResolveSingleAttributeMappingAsync(
                source,
                mapping,
                fallbackSourcesByMappingId,
                request,
                result,
                cancellationToken));
        }

        var effectiveSku = resolved
            .FirstOrDefault(mapped =>
                mapped.HasValue &&
                mapped.TargetType == NopTargetType.ProductField &&
                string.Equals(
                    mapped.Mapping.NopTargetKey,
                    "Sku",
                    StringComparison.OrdinalIgnoreCase))
            ?.DisplayValue;

        effectiveSku ??= source.Identifier ?? source.Code;

        foreach (var mapping in mappings.Where(mapping =>
                     mapping.ValueModeId ==
                     (int)AkeneoAttributeMappingValueMode.Template))
        {
            cancellationToken.ThrowIfCancellationRequested();

            resolved.Add(ResolveTemplateMapping(
                source,
                mapping,
                effectiveSku,
                mappingFamilyCode,
                request,
                result));
        }

        return resolved;
    }

    private async Task<AkeneoResolvedMappedValue>
        ResolveSingleAttributeMappingAsync(
            AkeneoProductDefinition source,
            AkeneoAttributeMapping mapping,
            IReadOnlyDictionary<int, IReadOnlyList<AkeneoAttributeMappingFallbackSource>> fallbackSourcesByMappingId,
            AkeneoProductImportRequest request,
            AkeneoProductImportResult result,
            CancellationToken cancellationToken)
    {
        var targetType = (NopTargetType)mapping.NopTargetTypeId;

        if (targetType == NopTargetType.Ignore)
        {
            return new AkeneoResolvedMappedValue
            {
                Mapping = mapping,
                HasValue = false
            };
        }

        var locale = !string.IsNullOrWhiteSpace(mapping.Locale)
            ? mapping.Locale
            : request.Locale;
        var channel = !string.IsNullOrWhiteSpace(mapping.Channel)
            ? mapping.Channel
            : request.Channel;

        AkeneoResolvedProductValue value = null;
        var resolvedSuccessfully = false;
        var resolvedSourceDisplayName =
            AkeneoMappingHelper.GetSourceDisplayName(mapping);

        foreach (var sourceMapping in BuildOrderedSourceMappings(
                     mapping,
                     fallbackSourcesByMappingId))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var candidateValue = await ResolveSourceValueAsync(
                source,
                sourceMapping,
                locale,
                channel,
                request.Currency,
                cancellationToken);

            if (!HasDisplayValue(candidateValue))
                continue;

            value = candidateValue;
            resolvedSuccessfully = true;
            resolvedSourceDisplayName =
                AkeneoMappingHelper.GetSourceDisplayName(sourceMapping);
            break;
        }

        (resolvedSuccessfully, value) = ApplyTransform(
            mapping,
            value,
            resolvedSuccessfully,
            resolvedSourceDisplayName,
            result);

        var hasValue = resolvedSuccessfully && HasDisplayValue(value);

        if (!hasValue && mapping.IsRequired)
        {
            result.AddError(
                $"Required Akeneo source is missing a value. Tried: {GetSourceChainDisplayName(mapping, fallbackSourcesByMappingId)}");
        }

        return new AkeneoResolvedMappedValue
        {
            Mapping = mapping,
            HasValue = hasValue,
            Value = value,
            ResolvedSourceDisplayName = hasValue
                ? resolvedSourceDisplayName
                : null
        };
    }

    private AkeneoResolvedMappedValue ResolveTemplateMapping(
        AkeneoProductDefinition source,
        AkeneoAttributeMapping mapping,
        string effectiveSku,
        string mappingFamilyCode,
        AkeneoProductImportRequest request,
        AkeneoProductImportResult result)
    {
        var sourceDisplayName =
            $"Template '{mapping.Name ?? mapping.MappingKey}'";

        if ((NopTargetType)mapping.NopTargetTypeId ==
            NopTargetType.Ignore)
        {
            return new AkeneoResolvedMappedValue
            {
                Mapping = mapping,
                HasValue = false,
                ResolvedSourceDisplayName = sourceDisplayName
            };
        }

        var locale = !string.IsNullOrWhiteSpace(mapping.Locale)
            ? mapping.Locale
            : request.Locale;
        var channel = !string.IsNullOrWhiteSpace(mapping.Channel)
            ? mapping.Channel
            : request.Channel;

        var rendered = valueTemplateRenderer.Render(
            mapping.ValueTemplate,
            new AkeneoValueTemplateContext
            {
                Source = source,
                Locale = locale,
                Channel = channel,
                Currency = request.Currency,
                FamilyCode = mappingFamilyCode,
                Sku = effectiveSku
            });

        var resolvedSuccessfully = rendered.Success &&
            !string.IsNullOrWhiteSpace(rendered.Value);
        AkeneoResolvedProductValue value = resolvedSuccessfully
            ? new AkeneoResolvedProductValue
            {
                AttributeCode = mapping.MappingKey,
                Locale = locale,
                Channel = channel,
                Currency = request.Currency,
                SourceAttributeType = "computed_template",
                DisplayValue = rendered.Value,
                DisplayValues = new[] { rendered.Value }
            }
            : null;

        if (rendered.Errors.Count > 0)
        {
            var message =
                $"Could not render {sourceDisplayName}: {string.Join(" ", rendered.Errors)}";

            if (mapping.IsRequired)
                result.AddError(message);
            else
                result.AddWarning(message);
        }
        else if (rendered.MissingTokens.Count > 0)
        {
            var message =
                $"Could not render {sourceDisplayName} because these tokens had no value: {string.Join(", ", rendered.MissingTokens.Select(token => $"{{{token}}}"))}.";

            if (mapping.IsRequired)
                result.AddError(message);
            else
                result.AddWarning(message);
        }
        else if (!resolvedSuccessfully && mapping.IsRequired)
        {
            result.AddError(
                $"Required {sourceDisplayName} rendered an empty value.");
        }

        (resolvedSuccessfully, value) = ApplyTransform(
            mapping,
            value,
            resolvedSuccessfully,
            sourceDisplayName,
            result);

        var hasValue = resolvedSuccessfully && HasDisplayValue(value);

        return new AkeneoResolvedMappedValue
        {
            Mapping = mapping,
            HasValue = hasValue,
            Value = value,
            ResolvedSourceDisplayName = hasValue
                ? sourceDisplayName
                : null
        };
    }

    private (bool Success, AkeneoResolvedProductValue Value) ApplyTransform(
        AkeneoAttributeMapping mapping,
        AkeneoResolvedProductValue value,
        bool resolvedSuccessfully,
        string sourceDisplayName,
        AkeneoProductImportResult result)
    {
        if (!resolvedSuccessfully ||
            value == null ||
            string.IsNullOrWhiteSpace(mapping.TransformRuleJson))
        {
            return (resolvedSuccessfully, value);
        }

        var transformed = transformationService.Transform(value, mapping);

        if (transformed.Success)
            return (true, transformed.Value);

        var message =
            $"Transform failed for Akeneo source '{sourceDisplayName}': {transformed.Error}";

        if (mapping.IsRequired)
            result.AddError(message);
        else
            result.AddWarning(message);

        return (false, null);
    }

    private async Task<AkeneoResolvedProductValue> ResolveSourceValueAsync(
        AkeneoProductDefinition source,
        AkeneoAttributeMapping sourceMapping,
        string locale,
        string channel,
        string currency,
        CancellationToken cancellationToken)
    {
        if (IsReferenceEntityMapping(sourceMapping) &&
            !string.IsNullOrWhiteSpace(
                sourceMapping.AkeneoReferenceEntityAttributeCode))
        {
            return await referenceEntityValueResolver.ResolveAsync(
                source,
                sourceMapping,
                locale,
                channel,
                currency,
                cancellationToken);
        }

        return productValueResolver.TryGetValue(
            source,
            sourceMapping.AkeneoAttributeCode,
            out var value,
            locale,
            channel,
            currency)
                ? value
                : null;
    }

    private static IEnumerable<AkeneoAttributeMapping>
        BuildOrderedSourceMappings(
            AkeneoAttributeMapping primaryMapping,
            IReadOnlyDictionary<int, IReadOnlyList<AkeneoAttributeMappingFallbackSource>> fallbackSourcesByMappingId)
    {
        yield return primaryMapping;

        if (!fallbackSourcesByMappingId.TryGetValue(
                primaryMapping.Id,
                out var fallbackSources))
        {
            yield break;
        }

        foreach (var fallback in fallbackSources
                     .OrderBy(source => source.DisplayOrder)
                     .ThenBy(source => source.Id))
        {
            yield return new AkeneoAttributeMapping
            {
                AkeneoAttributeCode = fallback.AkeneoAttributeCode,
                AkeneoAttributeTypeId = fallback.AkeneoAttributeTypeId,
                AkeneoReferenceEntityCode =
                    fallback.AkeneoReferenceEntityCode,
                AkeneoReferenceEntityAttributeCode =
                    fallback.AkeneoReferenceEntityAttributeCode
            };
        }
    }

    private static string GetSourceChainDisplayName(
        AkeneoAttributeMapping primaryMapping,
        IReadOnlyDictionary<int, IReadOnlyList<AkeneoAttributeMappingFallbackSource>> fallbackSourcesByMappingId)
    {
        return string.Join(
            " → ",
            BuildOrderedSourceMappings(
                    primaryMapping,
                    fallbackSourcesByMappingId)
                .Select(AkeneoMappingHelper.GetSourceDisplayName));
    }

    private static bool HasDisplayValue(
        AkeneoResolvedProductValue value)
    {
        return !string.IsNullOrWhiteSpace(value?.DisplayValue) ||
               value?.DisplayValues is { Count: > 0 };
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
        IReadOnlyDictionary<int, IReadOnlyList<AkeneoAttributeMappingFallbackSource>> fallbackSourcesByMappingId,
        IReadOnlyCollection<string> handledAssetAttributeCodes,
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

        var templateAttributeCodes = effectiveMappings
            .Where(mapping => mapping.ValueModeId ==
                (int)AkeneoAttributeMappingValueMode.Template)
            .SelectMany(mapping => valueTemplateRenderer
                .Validate(mapping.ValueTemplate)
                .ReferencedAttributeCodes);

        var handledCodes = effectiveMappings
            .Where(mapping => mapping.ValueModeId !=
                (int)AkeneoAttributeMappingValueMode.Template)
            .Where(mapping => !string.IsNullOrWhiteSpace(mapping.AkeneoAttributeCode))
            .Select(mapping => mapping.AkeneoAttributeCode.Trim())
            .Concat(
                fallbackSourcesByMappingId.Values
                    .SelectMany(sources => sources)
                    .Where(source => !string.IsNullOrWhiteSpace(
                        source.AkeneoAttributeCode))
                    .Select(source => source.AkeneoAttributeCode.Trim()))
            .Concat(templateAttributeCodes)
            .Concat(handledAssetAttributeCodes ?? Array.Empty<string>())
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
