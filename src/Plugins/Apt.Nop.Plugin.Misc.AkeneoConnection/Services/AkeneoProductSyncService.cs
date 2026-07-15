using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Nop.Core.Domain.Catalog;
using Nop.Services.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public class AkeneoProductSyncService(
    IAkeneoProductValueResolver productValueResolver,
    IAkeneoAttributeMappingService attributeMappingService,
    IAkeneoNopEntityMappingService entityMappingService,
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

        var mappedValues = ResolveMappedValues(
            source,
            mappings,
            request,
            result);

        var sku = ResolveSku(
            source,
            sourceEntityType,
            mappedValues);

        result.Sku = sku;

        Product existingProduct = null;

        if (result.Success)
        {
            existingProduct = await ResolveNopProductAsync(
                sourceEntityType,
                sourceCode,
                sourceUuid,
                sku,
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
            ExistingProduct = existingProduct
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

    private IList<AkeneoResolvedMappedValue> ResolveMappedValues(
        AkeneoProductDefinition source,
        IList<AkeneoAttributeMapping> mappings,
        AkeneoProductImportRequest request,
        AkeneoProductImportResult result)
    {
        var resolved = new List<AkeneoResolvedMappedValue>();

        foreach (var mapping in mappings)
        {
            var targetType = (NopTargetType)mapping.NopTargetTypeId;

            if (targetType == NopTargetType.Ignore)
                continue;

            var resolvedSuccessfully =
                productValueResolver.TryGetValue(
                    source,
                    mapping.AkeneoAttributeCode,
                    out var value,
                    !string.IsNullOrWhiteSpace(mapping.Locale)
                        ? mapping.Locale
                        : request.Locale,
                    !string.IsNullOrWhiteSpace(mapping.Channel)
                        ? mapping.Channel
                        : request.Channel,
                    request.Currency);

            var hasDisplayValue =
                !string.IsNullOrWhiteSpace(value?.DisplayValue) ||
                value?.DisplayValues is { Count: > 0 };

            var hasValue =
                resolvedSuccessfully &&
                hasDisplayValue;

            if (!hasValue && mapping.IsRequired)
            {
                result.AddError(
                    $"Required Akeneo attribute is missing a value: " +
                    mapping.AkeneoAttributeCode);
            }

            // Keep missing optional mappings. The section synchronizers need
            // these to know when an existing value should be cleared.
            resolved.Add(new AkeneoResolvedMappedValue
            {
                Mapping = mapping,
                HasValue = hasValue,
                Value = value
            });
        }

        return resolved;
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

    private async Task<Product> ResolveNopProductAsync(
        AkeneoEntityType sourceEntityType,
        string sourceCode,
        string sourceUuid,
        string sku,
        AkeneoProductImportResult result)
    {
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
                return product;
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
                return product;
        }

        if (!string.IsNullOrWhiteSpace(sku))
        {
            var product = await productService
                .GetProductBySkuAsync(sku);

            if (product != null)
            {
                result.AddMessage(
                    $"Matched existing nopCommerce product by SKU '{sku}'.");
            }

            return product;
        }

        return null;
    }

    private async Task<Product> GetMappedProductOrWarnAsync(
        int? mappedProductId,
        string mappingDescription,
        AkeneoProductImportResult result)
    {
        if (!mappedProductId.HasValue)
            return null;

        var product = await productService
            .GetProductByIdAsync(mappedProductId.Value);

        if (product != null)
            return product;

        result.AddWarning(
            $"A mapping exists for {mappingDescription}, but " +
            $"nopCommerce product ID {mappedProductId.Value} was not found.");

        return null;
    }
}