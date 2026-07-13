using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Transactions;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Extensions;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Humanizer;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Data;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Seo;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public class AkeneoProductImportService(
    IAkeneoApiClient akeneoApiClient,
    IAkeneoProductValueResolver productValueResolver,
    IAkeneoAttributeMappingService attributeMappingService,
    IAkeneoNopEntityMappingService entityMappingService,
    IAkeneoSyncItemLogService syncItemLogService,
    IProductService productService,
    ICategoryService categoryService,
    IManufacturerService manufacturerService,
    ISpecificationAttributeService specificationAttributeService,
    IProductAttributeService productAttributeService,
    IGenericAttributeService genericAttributeService,
    IUrlRecordService urlRecordService,
    IAkeneoFamilyMappingService familyMappingService,
    IAkeneoVariantRelationshipService variantRelationshipService,
    IRepository<ProductAttributeValue> productAttributeValueRepository)
    : IAkeneoProductImportService
{

    public async Task<AkeneoProductBatchImportResult> ImportProductsAsync(
    AkeneoProductBatchImportRequest request,
    CancellationToken cancellationToken = default)
    {
        request ??= new AkeneoProductBatchImportRequest();

        if (request.SyncRunRecordId <= 0)
        {
            throw new ArgumentException(
                "SyncRunRecordId must be provided in the request.",
                nameof(request));
        }

        var batchResult = new AkeneoProductBatchImportResult
        {
            SyncRunRecordId = request.SyncRunRecordId
        };

        var pageSize = request.PageSize <= 0 ? 100 : request.PageSize;
        var searchAfter = request.SearchAfter;
        var parentProductCache = new Dictionary<string, Product>(StringComparer.OrdinalIgnoreCase);
        var subModelDefinitionCache = new Dictionary<string, AkeneoProductDefinition>(StringComparer.OrdinalIgnoreCase);
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (request.MaxProducts.HasValue &&
                    batchResult.TotalRead >= request.MaxProducts.Value)
                {
                    batchResult.AddMessage(
                        $"Stopped after reaching MaxProducts limit of {request.MaxProducts.Value}.");

                    return batchResult;
                }

                var effectivePageSize = pageSize;

                if (request.MaxProducts.HasValue)
                {
                    var remaining = request.MaxProducts.Value - batchResult.TotalRead;
                    effectivePageSize = Math.Min(pageSize, remaining);
                }

                var page = await akeneoApiClient.GetProductsPageAsync(
                    effectivePageSize,
                    searchAfter,
                    request.SearchJson,
                    cancellationToken);

                if (page?.Items == null || !page.Items.Any())
                    break;

                foreach (var akeneoProduct in page.Items)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (request.MaxProducts.HasValue &&
                        batchResult.TotalRead >= request.MaxProducts.Value)
                    {
                        batchResult.AddMessage(
                            $"Stopped after reaching MaxProducts limit of {request.MaxProducts.Value}.");

                        return batchResult;
                    }

                    batchResult.TotalRead++;

                    var itemResult = new AkeneoProductImportResult
                    {
                        AkeneoProductUuid = akeneoProduct.Uuid,
                        AkeneoIdentifier = akeneoProduct.Identifier,
                        AkeneoProductKey =
                            akeneoProduct.Uuid ??
                            akeneoProduct.Identifier,
                        ActionType = SyncItemActionType.Skipped
                    };

                    string rawPayloadSnapshot = null;

                    try
                    {
                        if (request.SaveRawPayloadSnapshot)
                            rawPayloadSnapshot = akeneoProduct.Values.GetRawText().Truncate(12000);


                        using (var scope = CreateUnitTransaction())
                        {
                            itemResult = await ImportProductAsync(akeneoProduct, request, itemResult, parentProductCache, subModelDefinitionCache);

                            if (!itemResult.Errors.Any() && itemResult.ActionType != SyncItemActionType.Failed)
                                scope.Complete();
                        }

                        // Unit rolled back if it didn't complete — don't report a stale product id.
                        if (itemResult.Errors.Any() || itemResult.ActionType == SyncItemActionType.Failed)
                            itemResult.NopProductId = 0;

                        ApplyItemResultToBatchResult(batchResult, itemResult);

                        // Logged outside the scope so the item log survives a rolled-back import.
                        await SaveBatchItemLogIfNeededAsync(request, itemResult, rawPayloadSnapshot);

                        if (ShouldKeepItemResultInMemory(itemResult))
                            batchResult.LoggedItemResults.Add(itemResult);
                    }
                    catch (Exception ex)
                    {
                        // Exception inside the using already disposed the scope → rolled back.
                        itemResult.AddError(ex.Message);
                        itemResult.ActionType = SyncItemActionType.Failed;
                        itemResult.NopProductId = 0;

                        ApplyItemResultToBatchResult(batchResult, itemResult);
                        await SaveBatchItemLogIfNeededAsync(request, itemResult, rawPayloadSnapshot);
                        batchResult.LoggedItemResults.Add(itemResult);

                        if (!request.ContinueOnError)
                            throw;
                    }
                }

                if (!page.HasNextPage)
                    break;

                searchAfter = page.SearchAfter;
            }

            batchResult.AddMessage(
                $"Product import completed. Read: {batchResult.TotalRead}, Created: {batchResult.CreatedCount}, Updated: {batchResult.UpdatedCount}, Skipped: {batchResult.SkippedCount}, Failed: {batchResult.FailedCount}.");

            return batchResult;
        }
        catch (OperationCanceledException)
        {
            batchResult.Canceled = true;
            batchResult.AddError("Product import was canceled.");
            return batchResult;
        }
        catch (Exception ex)
        {
            batchResult.AddError(ex.Message);
            return batchResult;
        }
    }


    public async Task<AkeneoProductImportResult> ImportProductByUuidAsync(
      AkeneoProductImportRequest request,
      CancellationToken cancellationToken = default)
    {
        var result = new AkeneoProductImportResult();

        request ??= new AkeneoProductImportRequest();
        if (request.SyncRunRecordId <= 0)
            throw new ArgumentException("SyncRunRecordId must be provided in the request.", nameof(request));

        result.AkeneoProductUuid = request.AkeneoProductUuid;

        string rawPayloadSnapshot = null;

        try
        {
            if (string.IsNullOrWhiteSpace(request.AkeneoProductUuid))
            {
                result.AddError("Akeneo product UUID is required.");
                result.ActionType = SyncItemActionType.Failed;
                return await SaveLogAndReturnAsync(request, result, null);
            }

            var akeneoProduct = await akeneoApiClient.GetProductByUuidAsync(
                request.AkeneoProductUuid,
                cancellationToken);

            if (akeneoProduct == null)
            {
                result.AddError($"Akeneo product was not found. UUID: {request.AkeneoProductUuid}");
                result.ActionType = SyncItemActionType.Failed;
                return await SaveLogAndReturnAsync(request, result, null);
            }

            result.AkeneoProductUuid = akeneoProduct.Uuid;
            result.AkeneoIdentifier = akeneoProduct.Identifier;

            if (request.SaveRawPayloadSnapshot)
                rawPayloadSnapshot = akeneoProduct.Values.GetRawText().Truncate(12000);

            var parentProductCache = new Dictionary<string, Product>(StringComparer.OrdinalIgnoreCase);
            var subModelDefinitionCache = new Dictionary<string, AkeneoProductDefinition>(StringComparer.OrdinalIgnoreCase);
            using (var scope = CreateUnitTransaction())
            {
                result = await ImportProductAsync(akeneoProduct, request, result, parentProductCache, subModelDefinitionCache);

                if (!result.Errors.Any() && result.ActionType != SyncItemActionType.Failed)
                    scope.Complete();
            }

            if (result.Errors.Any() || result.ActionType == SyncItemActionType.Failed)
                result.NopProductId = 0;

            return await SaveLogAndReturnAsync(request, result, rawPayloadSnapshot);
        }
        catch (Exception ex)
        {
            result.AddError(ex.Message);
            result.ActionType = SyncItemActionType.Failed;
            result.NopProductId = 0;

            return await SaveLogAndReturnAsync(request, result, rawPayloadSnapshot);
        }
    }


    private async Task<AkeneoProductDefinition?> GetProductModelCachedAsync(
        string code,
        IDictionary<string, AkeneoProductDefinition> subModelJsonCache)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;

        if (subModelJsonCache.TryGetValue(code, out var cached))
            return cached;

        var model = await akeneoApiClient.GetProductModelByCodeAsync(code);
        if (model != null)
            subModelJsonCache[code] = model;

        return model;
    }

    private async Task<AkeneoProductImportResult> ImportProductAsync(
    AkeneoProductDefinition akeneoProduct,
    AkeneoProductImportRequest request,
    AkeneoProductImportResult result,
    IDictionary<string, Product> parentProductCache,
    IDictionary<string, AkeneoProductDefinition> subModelDefinitionCache)
    {
        var akeneoUuid = akeneoProduct.Uuid?.Trim();
        var akeneoIdentifier = akeneoProduct.Identifier?.Trim();

        if (string.IsNullOrWhiteSpace(akeneoUuid) && string.IsNullOrWhiteSpace(akeneoIdentifier))
        {
            result.AddError("Akeneo product does not contain a uuid or identifier.");
            result.ActionType = SyncItemActionType.Failed;
            return result;
        }

        var parentCode = akeneoProduct.Parent?.Trim();

        // Standalone (not part of a variant family) — existing flat behavior, unchanged.
        if (string.IsNullOrWhiteSpace(parentCode))
        {
            await UpsertNopProductCoreAsync(akeneoProduct, request, result);
            return result;
        }

        var familyCode = akeneoProduct.Family?.Trim();

        // Resolve the leaf ONCE for the whole variant path.
        var leaf = await ResolveLeafAsync(akeneoProduct, request, result);
        if (!result.Success)
        {
            result.ActionType = SyncItemActionType.Failed;
            return result;
        }

        AkeneoVariantRelationshipMode? forcedMode = null;

        var subModel = await GetProductModelCachedAsync(parentCode, subModelDefinitionCache);
        if (subModel != null)
        {
            var overrideRule = await ResolveSubModelOverrideAsync(familyCode, akeneoProduct, subModel);
            if (overrideRule != null)
            {
                if (overrideRule.VariantRelationshipOverrideMode == AkeneoVariantRelationshipMode.None)
                {
                    var existingMode = await ClassifyExistingLeafStructureAsync(leaf.ExistingProduct, leaf.Sku);

                    if (existingMode is null or AkeneoVariantRelationshipMode.None)
                    {
                        var itemToImport = overrideRule.MergeAncestorValues
                            ? await BuildFlattenedLeafAsync(akeneoProduct, subModel, subModelDefinitionCache)
                            : akeneoProduct;

                        await UpsertNopProductCoreAsync(
                            itemToImport,
                            request,
                            result,
                            leaf: overrideRule.MergeAncestorValues ? null : leaf);
                        return result;
                    }
                }
                else
                {
                    forcedMode = overrideRule.VariantRelationshipOverrideMode;
                }
            }
        }

        var parentProduct = await ResolveOrCreateParentProductAsync(
            parentCode, request, result, parentProductCache, subModelDefinitionCache);
        if (parentProduct == null)
        {
            result.ActionType = SyncItemActionType.Failed;
            return result;
        }

        var context = await BuildVariantImportContextAsync(
            akeneoProduct, akeneoIdentifier, familyCode, request, result, leaf.Sku);
        if (!result.Success)
        {
            result.ActionType = SyncItemActionType.Failed;
            return result;
        }

        context.VariantRelationshipModeOverride = forcedMode;

        AkeneoVariantImportResult variantResult;

        try
        {
            variantResult = await variantRelationshipService.ApplyAsync(
                parentProduct,
                context,
                upsertChildProductAsync: () => UpsertNopProductCoreAsync(akeneoProduct, request, result));
        }
        catch (NopException ex)
        {
            result.AddError(ex.Message);
            result.ActionType = SyncItemActionType.Failed;
            return result;
        }

        if (variantResult.Mode == AkeneoVariantRelationshipMode.ProductAttributeCombinations)
        {
            result.NopProductId = parentProduct.Id;
            result.ActionType = result.ActionType == SyncItemActionType.Failed
                ? result.ActionType
                : SyncItemActionType.Updated;

            await entityMappingService.UpsertAkeneoNopEntityMappingAsync(
                AkeneoEntityType.Product, akeneoIdentifier, akeneoUuid,
                NopEntityType.Product, parentProduct.Id);
        }
        else if (variantResult.NopProductId.HasValue)
        {
            result.NopProductId = variantResult.NopProductId.Value;
        }

        return result;
    }


    private async Task<AkeneoVariantRelationshipMode?> ClassifyExistingLeafStructureAsync(Product product, string sku)
    {
        if (product == null)
            return null;   // new product

        // Grouped child: it points at a parent grouped product.
        if (product.ParentGroupedProductId != 0)
            return AkeneoVariantRelationshipMode.GroupedProducts;

        // Combination child: its SKU is registered as a ProductAttributeCombination.
        if (!string.IsNullOrWhiteSpace(sku))
        {
            var combination = await productAttributeService.GetProductAttributeCombinationBySkuAsync(sku);
            if (combination != null)
                return AkeneoVariantRelationshipMode.ProductAttributeCombinations;
        }

        // Associated child: this product is referenced as an AssociatedToProduct attribute value.
        var isAssociated = await productAttributeValueRepository.Table.AnyAsync(v =>
            v.AttributeValueTypeId == (int)AttributeValueType.AssociatedToProduct &&
            v.AssociatedProductId == product.Id);

        if (isAssociated)
            return AkeneoVariantRelationshipMode.AssociatedToProductAttributeValue;

        // Plain simple — no parent structure. Safe to keep/flatten as standalone.
        return AkeneoVariantRelationshipMode.None;
    }

    private async Task<Product> UpsertNopProductCoreAsync(
        AkeneoProductDefinition akeneoProduct,
        AkeneoProductImportRequest request,
        AkeneoProductImportResult result,
        ResolvedLeaf leaf = null)
    {
        var akeneoUuid = akeneoProduct.Uuid?.Trim();
        var akeneoIdentifier = akeneoProduct.Identifier?.Trim();

        result.AkeneoProductUuid = akeneoUuid;
        result.AkeneoIdentifier = akeneoIdentifier;
        result.AkeneoProductKey = akeneoUuid ?? akeneoIdentifier;

        // Reuse the pre-resolved bundle when the caller supplied one (variant path);
        // resolve here for the standalone path and the merged-flatten path.
        leaf ??= await ResolveLeafAsync(akeneoProduct, request, result);

        if (!result.Success)
        {
            result.ActionType = SyncItemActionType.Failed;
            return null;
        }

        var mappedValues = leaf.MappedValues;
        var sku = leaf.Sku;
        result.Sku = sku;

        var product = leaf.ExistingProduct;   
        var isNew = product == null;

        if (isNew && !request.CreateNewProducts)
        {
            result.ActionType = SyncItemActionType.Skipped;
            result.AddMessage($"Product does not exist and CreateNewProducts is disabled. UUID: {akeneoUuid}, identifier: {akeneoIdentifier}");
            return null;
        }

        if (!isNew && !request.UpdateExistingProducts)
        {
            result.NopProductId = product.Id;
            result.ActionType = SyncItemActionType.Skipped;
            result.AddMessage($"Product exists and UpdateExistingProducts is disabled. Product ID: {product.Id}");
            return product;
        }

        product ??= CreateBaseProduct(sku, akeneoIdentifier ?? akeneoUuid);

        var productChanged = ApplyProductFields(product, mappedValues, result);

        if (isNew)
        {
            product.CreatedOnUtc = DateTime.UtcNow;
            product.UpdatedOnUtc = DateTime.UtcNow;
            await productService.InsertProductAsync(product);
            result.NopProductId = product.Id;
            result.ActionType = SyncItemActionType.Created;
            result.AddMessage($"Created nopCommerce product ID {product.Id}.");
        }
        else if (productChanged)
        {
            product.UpdatedOnUtc = DateTime.UtcNow;
            await productService.UpdateProductAsync(product);
            result.NopProductId = product.Id;
            result.ActionType = SyncItemActionType.Updated;
            result.AddMessage($"Updated nopCommerce product fields for product ID {product.Id}.");
        }
        else
        {
            result.NopProductId = product.Id;
            result.ActionType = SyncItemActionType.Skipped;
        }

        await entityMappingService.UpsertAkeneoNopEntityMappingAsync(
            AkeneoEntityType.Product, akeneoIdentifier, akeneoUuid, NopEntityType.Product, product.Id);

        var relatedDataChanged = false;

        relatedDataChanged |= await ApplySeoFieldsAsync(product, mappedValues, isNew, result);

        if (request.AddMappedCategories)
        {
            relatedDataChanged |= await ApplyRootCategoryMappingsAsync(product, akeneoProduct, result);
            relatedDataChanged |= await ApplyAttributeCategoryMappingsAsync(product, mappedValues, result);
        }

        relatedDataChanged |= await ApplySpecificationAttributesAsync(product, mappedValues, request, result);
        relatedDataChanged |= await ApplyProductAttributesAsync(product, mappedValues, request, result);
        relatedDataChanged |= await ApplyCustomPropertiesAsync(product, mappedValues, result);

        if (!isNew && result.ActionType == SyncItemActionType.Skipped && relatedDataChanged)
            result.ActionType = SyncItemActionType.Updated;

        if (result.ActionType == SyncItemActionType.Skipped)
        {
            result.Messages.Clear();
            result.AddMessage("No product, SEO, category, specification, product attribute, manufacturer, or custom property changes detected.");
        }

        return product;
    }

    private IList<ResolvedMappedValue> ResolveMappedValues(
        AkeneoProductDefinition akeneoProduct,
        IList<AkeneoAttributeMapping> mappings,
        AkeneoProductImportRequest request,
        AkeneoProductImportResult result)
    {
        var resolved = new List<ResolvedMappedValue>();

        foreach (var mapping in mappings)
        {
            var targetType = (NopTargetType)mapping.NopTargetTypeId;

            if (targetType == NopTargetType.Ignore)
                continue;

            var hasValue = productValueResolver.TryGetValue(
                akeneoProduct,
                mapping.AkeneoAttributeCode,
                out var value,
                !string.IsNullOrWhiteSpace(mapping.Locale) ? mapping.Locale : request.Locale,
                !string.IsNullOrWhiteSpace(mapping.Channel) ? mapping.Channel : request.Channel,
                request.Currency);

            var hasDisplay =
                !string.IsNullOrWhiteSpace(value?.DisplayValue) ||
                value?.DisplayValues is { Count: > 0 };

            if (!hasValue || !hasDisplay)
            {
                if (mapping.IsRequired)
                    result.AddError($"Required Akeneo attribute is missing a value: {mapping.AkeneoAttributeCode}");

                continue;
            }

            resolved.Add(new ResolvedMappedValue
            {
                Mapping = mapping,
                TargetType = targetType,
                Value = value
            });
        }

        return resolved;
    }

    private async Task<Product> ResolveNopProductAsync(
     string akeneoUuid,
     string akeneoIdentifier,
     string sku,
     AkeneoProductImportResult result)
    {
        if (!string.IsNullOrWhiteSpace(akeneoUuid))
        {
            var mappedProductId = await entityMappingService.GetMappedNopEntityIdByAkeneoUuidAsync(
                AkeneoEntityType.Product,
                akeneoUuid,
                NopEntityType.Product);

            var mappedProduct = await GetMappedProductOrWarnAsync(
                mappedProductId,
                $"Akeneo UUID {akeneoUuid}",
                result);

            if (mappedProduct != null)
                return mappedProduct;
        }

        if (!string.IsNullOrWhiteSpace(akeneoIdentifier))
        {
            var mappedProductId = await entityMappingService.GetMappedNopEntityIdByAkeneoCodeAsync(
                AkeneoEntityType.Product,
                akeneoIdentifier,
                NopEntityType.Product);

            var mappedProduct = await GetMappedProductOrWarnAsync(
                mappedProductId,
                $"Akeneo identifier {akeneoIdentifier}",
                result);

            if (mappedProduct != null)
                return mappedProduct;
        }

        if (!string.IsNullOrWhiteSpace(sku))
        {
            var productBySku = await productService.GetProductBySkuAsync(sku);

            if (productBySku != null)
            {
                result.AddMessage(
                    $"Matched existing nopCommerce product by SKU '{sku}'. UUID mapping will be saved after import.");

                return productBySku;
            }
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

        var product = await productService.GetProductByIdAsync(mappedProductId.Value);

        if (product != null)
            return product;

        result.AddWarning(
            $"Product mapping exists for {mappingDescription}, but nopCommerce product ID {mappedProductId.Value} was not found.");

        return null;
    }

    private static Product CreateBaseProduct(string sku, string akeneoProductKey)
    {
        return new Product
        {
            ProductType = ProductType.SimpleProduct,
            VisibleIndividually = true,
            Sku = string.IsNullOrWhiteSpace(sku) ? akeneoProductKey : sku,
            Name = string.IsNullOrWhiteSpace(sku) ? akeneoProductKey : sku,
            Published = true,
            Deleted = false,
            CreatedOnUtc = DateTime.UtcNow,
            UpdatedOnUtc = DateTime.UtcNow
        };
    }

    private static string ResolveSku(
        AkeneoProductDefinition akeneoProduct,
        IList<ResolvedMappedValue> mappedValues)
    {
        var mappedSku = mappedValues
            .FirstOrDefault(value =>
                value.TargetType == NopTargetType.ProductField &&
                string.Equals(value.Mapping.NopTargetKey, "Sku", StringComparison.OrdinalIgnoreCase))
            ?.Value
            ?.DisplayValue;

        if (!string.IsNullOrWhiteSpace(mappedSku))
            return mappedSku.Trim();

        return akeneoProduct.Identifier?.Trim();
    }

    private static bool ApplyProductFields(
    Product product,
    IList<ResolvedMappedValue> mappedValues,
    AkeneoProductImportResult result)
    {
        var changed = false;

        foreach (var mappedValue in mappedValues.Where(value => value.TargetType == NopTargetType.ProductField))
        {
            var targetKey = mappedValue.Mapping.NopTargetKey?.Trim();
            var value = ApplyTransform(
                mappedValue.Value.DisplayValue,
                mappedValue.Mapping.TransformRuleJson);

            if (string.IsNullOrWhiteSpace(targetKey))
            {
                result.AddWarning(
                    $"Product field mapping has no target key. Akeneo attribute: {mappedValue.Mapping.AkeneoAttributeCode}");

                continue;
            }

            switch (targetKey)
            {
                case "Name":
                    changed |= AkeneoMappingHelper.SetIfChanged(
                        product.Name,
                        value,
                        newValue => product.Name = newValue);
                    break;

                case "ShortDescription":
                    changed |= AkeneoMappingHelper.SetIfChanged(
                        product.ShortDescription,
                        value,
                        newValue => product.ShortDescription = newValue);
                    break;

                case "FullDescription":
                    changed |= AkeneoMappingHelper.SetIfChanged(
                        product.FullDescription,
                        value,
                        newValue => product.FullDescription = newValue);
                    break;

                case "Sku":
                    changed |= AkeneoMappingHelper.SetIfChanged(
                        product.Sku,
                        value,
                        newValue => product.Sku = newValue);
                    break;

                case "Price":
                    if (TryParseDecimal(value, out var price))
                    {
                        changed |= AkeneoMappingHelper.SetIfChanged(
                            product.Price,
                            price,
                            newValue => product.Price = newValue);
                    }
                    else
                    {
                        result.AddWarning(
                            $"Could not parse price value '{value}' from {mappedValue.Mapping.AkeneoAttributeCode}.");
                    }

                    break;

                case "Gtin":
                    changed |= AkeneoMappingHelper.SetIfChanged(
                        product.Gtin,
                        value,
                        newValue => product.Gtin = newValue);
                    break;

                case "ManufacturerPartNumber":
                    changed |= AkeneoMappingHelper.SetIfChanged(
                        product.ManufacturerPartNumber,
                        value,
                        newValue => product.ManufacturerPartNumber = newValue);
                    break;

                case "Published":
                    if (TryParseBoolean(value, out var published))
                    {
                        changed |= AkeneoMappingHelper.SetIfChanged(
                            product.Published,
                            published,
                            newValue => product.Published = newValue);
                    }
                    else
                    {
                        result.AddWarning(
                            $"Could not parse published value '{value}' from {mappedValue.Mapping.AkeneoAttributeCode}.");
                    }

                    break;

                default:
                    result.AddWarning($"Unsupported product field target key: {targetKey}");
                    break;
            }
        }

        return changed;
    }

    private async Task<bool> ApplySeoFieldsAsync(
     Product product,
     IList<ResolvedMappedValue> mappedValues,
     bool isNew,
     AkeneoProductImportResult result)
    {
        var changed = false;
        string requestedSeName = null;
        var hasSeNameMapping = false;

        foreach (var mappedValue in mappedValues.Where(value => value.TargetType == NopTargetType.SeoField))
        {
            var targetKey = mappedValue.Mapping.NopTargetKey?.Trim();
            var value = ApplyTransform(mappedValue.Value.DisplayValue, mappedValue.Mapping.TransformRuleJson);

            if (string.IsNullOrWhiteSpace(targetKey))
            {
                result.AddWarning($"SEO mapping has no target key. Akeneo attribute: {mappedValue.Mapping.AkeneoAttributeCode}");
                continue;
            }

            switch (targetKey)
            {
                case "MetaTitle":
                    changed |= AkeneoMappingHelper.SetIfChanged(product.MetaTitle, value, v => product.MetaTitle = v);
                    break;
                case "MetaDescription":
                    changed |= AkeneoMappingHelper.SetIfChanged(product.MetaDescription, value, v => product.MetaDescription = v);
                    break;
                case "MetaKeywords":
                    changed |= AkeneoMappingHelper.SetIfChanged(product.MetaKeywords, value, v => product.MetaKeywords = v);
                    break;
                case "SeName":
                    hasSeNameMapping = true;
                    requestedSeName = value;
                    break;
                default:
                    result.AddWarning($"Unsupported SEO field target key: {targetKey}");
                    break;
            }
        }

        if (changed)
        {
            product.UpdatedOnUtc = DateTime.UtcNow;
            await productService.UpdateProductAsync(product);
            result.AddMessage($"Updated SEO meta fields for nopCommerce product ID {product.Id}.");
        }

        var slugChanged = await ApplySlugAsync(product, requestedSeName, hasSeNameMapping, isNew, result);

        return changed || slugChanged;
    }

    private async Task<bool> ApplySlugAsync(
        Product product,
        string requestedSeName,
        bool hasSeNameMapping,
        bool isNew,
        AkeneoProductImportResult result)
    {
        // Manage the slug from Akeneo when it's mapped; for new products without a mapping,
        // still guarantee a slug so the product is reachable. Existing products without a
        // SeName mapping keep whatever slug they have.
        if (!hasSeNameMapping && !isNew)
            return false;

        var seName = await urlRecordService.ValidateSeNameAsync(
            product,
            requestedSeName ?? string.Empty,
            product.Name,
            ensureNotEmpty: true);

        var currentSeName = await urlRecordService.GetActiveSlugAsync(product.Id, nameof(Product), 0);

        if (string.Equals(currentSeName, seName, StringComparison.Ordinal))
            return false;

        await urlRecordService.SaveSlugAsync(product, seName, 0);
        result.AddMessage($"Set product URL slug '{seName}'.");
        return true;
    }

    private async Task<bool> ApplyRootCategoryMappingsAsync(
      Product product,
      AkeneoProductDefinition akeneoProduct,
      AkeneoProductImportResult result)
    {
        var changed = false;
        var categoryCodes = akeneoProduct.Categories?
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string>();

        foreach (var categoryCode in categoryCodes)
        {
            var nopCategoryId = await entityMappingService.GetMappedNopEntityIdByAkeneoCodeAsync(
                AkeneoEntityType.Category,
                categoryCode,
                NopEntityType.Category);

            if (!nopCategoryId.HasValue)
            {
                result.AddWarning($"No nopCommerce category mapping found for Akeneo category '{categoryCode}'.");
                continue;
            }

            var categoryAdded = await AddProductCategoryIfMissingAsync(
                product.Id,
                nopCategoryId.Value);

            if (!categoryAdded)
                continue;

            changed = true;
            result.AddMessage($"Assigned category {nopCategoryId.Value} from Akeneo category '{categoryCode}'.");
        }

        return changed;
    }

    private async Task<bool> ApplyAttributeCategoryMappingsAsync(
        Product product,
        IList<ResolvedMappedValue> mappedValues,
        AkeneoProductImportResult result)
    {
        var changed = false;

        foreach (var mappedValue in mappedValues.Where(value => value.TargetType == NopTargetType.Category))
        {
            foreach (var categoryCode in GetRawValueItems(mappedValue.Value))
            {
                var nopCategoryId = await entityMappingService.GetMappedNopEntityIdByAkeneoCodeAsync(
                    AkeneoEntityType.Category,
                    categoryCode,
                    NopEntityType.Category);

                if (!nopCategoryId.HasValue)
                {
                    result.AddWarning(
                        $"No nopCommerce category mapping found for Akeneo category value '{categoryCode}' from attribute {mappedValue.Mapping.AkeneoAttributeCode}.");

                    continue;
                }

                var categoryAdded = await AddProductCategoryIfMissingAsync(
                    product.Id,
                    nopCategoryId.Value);

                if (!categoryAdded)
                    continue;

                changed = true;
                result.AddMessage($"Assigned category {nopCategoryId.Value} from attribute {mappedValue.Mapping.AkeneoAttributeCode}.");
            }
        }

        return changed;
    }

    private async Task<bool> AddProductCategoryIfMissingAsync(
        int productId,
        int categoryId)
    {
        var existingCategories = await categoryService.GetProductCategoriesByProductIdAsync(
            productId,
            showHidden: true);

        if (existingCategories.Any(category => category.CategoryId == categoryId))
            return false;

        await categoryService.InsertProductCategoryAsync(new ProductCategory
        {
            ProductId = productId,
            CategoryId = categoryId,
            DisplayOrder = 0
        });

        return true;
    }

    //private async Task ApplyManufacturerMappingsAsync(
    //    Product product,
    //    IList<ResolvedMappedValue> mappedValues,
    //    AkeneoProductImportResult result)
    //{
    //    foreach (var mappedValue in mappedValues.Where(value => value.TargetType == NopTargetType.Manufacturer))
    //    {
    //        foreach (var akeneoValueCode in GetRawValueItems(mappedValue.Value))
    //        {
    //            var manufacturerId = await entityMappingService.GetMappedNopEntityIdAsync(
    //                AkeneoEntityType.AttributeOption,
    //                akeneoValueCode,
    //                NopEntityType.Manufacturer);

    //            if (!manufacturerId.HasValue)
    //            {
    //                result.AddWarning(
    //                    $"No manufacturer mapping found for Akeneo value '{akeneoValueCode}' from attribute {mappedValue.Mapping.AkeneoAttributeCode}.");

    //                continue;
    //            }

    //            await AddProductManufacturerIfMissingAsync(product.Id, manufacturerId.Value);
    //            result.AddMessage($"Assigned manufacturer {manufacturerId.Value} from Akeneo value '{akeneoValueCode}'.");
    //        }
    //    }
    //}

    private async Task AddProductManufacturerIfMissingAsync(
        int productId,
        int manufacturerId)
    {
        var existingManufacturers = await manufacturerService.GetProductManufacturersByProductIdAsync(
            productId,
            showHidden: true);

        if (existingManufacturers.Any(manufacturer => manufacturer.ManufacturerId == manufacturerId))
            return;

        await manufacturerService.InsertProductManufacturerAsync(new ProductManufacturer
        {
            ProductId = productId,
            ManufacturerId = manufacturerId,
            DisplayOrder = 0
        });
    }

    private async Task<bool> ApplySpecificationAttributesAsync(
    Product product,
    IList<ResolvedMappedValue> mappedValues,
    AkeneoProductImportRequest request,
    AkeneoProductImportResult result)
    {
        var changed = false;

        foreach (var mappedValue in mappedValues.Where(value => value.TargetType == NopTargetType.SpecificationAttribute))
        {
            var specificationAttributeId = mappedValue.Mapping.NopTargetEntityId ?? 0;

            if (specificationAttributeId <= 0)
            {
                result.AddWarning(
                    $"Specification attribute mapping has no nopCommerce specification attribute. Akeneo attribute: {mappedValue.Mapping.AkeneoAttributeCode}");

                continue;
            }

            foreach (var optionItem in GetResolvedOptionItems(mappedValue))
            {
                var option = await GetOrCreateSpecificationAttributeOptionAsync(
                    specificationAttributeId,
                    mappedValue.Mapping.AkeneoAttributeCode,
                    optionItem.AkeneoOptionCode,
                    optionItem.DisplayName,
                    request.CreateMissingSpecificationAttributeOptions);

                if (option == null)
                {
                    result.AddWarning(
                        $"Specification option '{optionItem.DisplayName}' was not found and could not be created. Akeneo attribute: {mappedValue.Mapping.AkeneoAttributeCode}");

                    continue;
                }

                var added = await AddProductSpecificationAttributeIfMissingAsync(
                    product.Id,
                    option.Id);

                if (!added)
                    continue;

                changed = true;
                result.AddMessage($"Assigned specification option '{option.Name}' to product.");
            }
        }

        return changed;
    }

    private async Task<bool> AddProductSpecificationAttributeIfMissingAsync(
        int productId,
        int specificationAttributeOptionId)
    {
        var existingProductSpecificationAttributes =
            await specificationAttributeService.GetProductSpecificationAttributesAsync(
                productId,
                specificationAttributeOptionId: specificationAttributeOptionId);

        if (existingProductSpecificationAttributes.Any(attribute =>
                attribute.SpecificationAttributeOptionId == specificationAttributeOptionId))
        {
            return false;
        }

        await specificationAttributeService.InsertProductSpecificationAttributeAsync(
            new ProductSpecificationAttribute
            {
                ProductId = productId,
                AttributeTypeId = (int)SpecificationAttributeType.Option,
                SpecificationAttributeOptionId = specificationAttributeOptionId,
                AllowFiltering = true,
                ShowOnProductPage = true,
                DisplayOrder = 0
            });

        return true;
    }

    private async Task<ProductAttributeValueChangeResult> GetOrCreateProductAttributeValueAsync(
     int productAttributeMappingId,
     string akeneoAttributeCode,
     string akeneoProductKey,
     string akeneoOptionCode,
     string valueName,
     bool createMissing)
    {
        if (string.IsNullOrWhiteSpace(valueName) &&
            string.IsNullOrWhiteSpace(akeneoOptionCode))
        {
            return new ProductAttributeValueChangeResult();
        }

        akeneoAttributeCode = akeneoAttributeCode?.Trim();
        akeneoProductKey = akeneoProductKey?.Trim();
        akeneoOptionCode = akeneoOptionCode?.Trim();
        valueName = valueName?.Trim() ?? akeneoOptionCode;

        var akeneoValueMappingCode = BuildProductAttributeValueMappingCode(
            akeneoProductKey,
            akeneoAttributeCode,
            akeneoOptionCode ?? valueName);

        if (!string.IsNullOrWhiteSpace(akeneoValueMappingCode))
        {
            var mappedValueId = await entityMappingService.GetMappedNopEntityIdByAkeneoCodeAsync(
                AkeneoEntityType.Option,
                akeneoValueMappingCode,
                NopEntityType.ProductAttributeValue);

            if (mappedValueId.HasValue)
            {
                var mappedValue = await productAttributeService.GetProductAttributeValueByIdAsync(
                    mappedValueId.Value);

                if (mappedValue != null &&
                    mappedValue.ProductAttributeMappingId == productAttributeMappingId)
                {
                    if (!string.Equals(mappedValue.Name, valueName, StringComparison.Ordinal))
                    {
                        mappedValue.Name = valueName;
                        await productAttributeService.UpdateProductAttributeValueAsync(mappedValue);

                        return new ProductAttributeValueChangeResult
                        {
                            Value = mappedValue,
                            Changed = true
                        };
                    }

                    return new ProductAttributeValueChangeResult
                    {
                        Value = mappedValue,
                        Changed = false
                    };
                }
            }
        }

        var existingValues = await productAttributeService.GetProductAttributeValuesAsync(
            productAttributeMappingId);

        var existingValue = existingValues.FirstOrDefault(value =>
            string.Equals(value.Name, valueName, StringComparison.OrdinalIgnoreCase));

        if (existingValue != null)
        {
            if (!string.IsNullOrWhiteSpace(akeneoValueMappingCode))
            {
                await entityMappingService.UpsertAkeneoNopEntityMappingAsync(
                    AkeneoEntityType.Option,
                    akeneoValueMappingCode,
                    null,
                    NopEntityType.ProductAttributeValue,
                    existingValue.Id);
            }

            return new ProductAttributeValueChangeResult
            {
                Value = existingValue,
                Changed = false
            };
        }

        if (!createMissing)
            return new ProductAttributeValueChangeResult();

        var newValue = new ProductAttributeValue
        {
            ProductAttributeMappingId = productAttributeMappingId,
            AttributeValueTypeId = (int)AttributeValueType.Simple,
            Name = valueName,
            DisplayOrder = 0
        };

        await productAttributeService.InsertProductAttributeValueAsync(newValue);

        if (!string.IsNullOrWhiteSpace(akeneoValueMappingCode))
        {
            await entityMappingService.UpsertAkeneoNopEntityMappingAsync(
                AkeneoEntityType.Option,
                akeneoValueMappingCode,
                null,
                NopEntityType.ProductAttributeValue,
                newValue.Id);
        }

        return new ProductAttributeValueChangeResult
        {
            Value = newValue,
            Changed = true
        };
    }

    private async Task<SpecificationAttributeOption> GetOrCreateSpecificationAttributeOptionAsync(
      int specificationAttributeId,
      string akeneoAttributeCode,
      string akeneoOptionCode,
      string optionName,
      bool createMissing)
    {
        if (specificationAttributeId <= 0)
            return null;

        if (string.IsNullOrWhiteSpace(optionName) &&
            string.IsNullOrWhiteSpace(akeneoOptionCode))
        {
            return null;
        }

        akeneoAttributeCode = akeneoAttributeCode?.Trim();
        akeneoOptionCode = akeneoOptionCode?.Trim();
        optionName = optionName?.Trim();

        var akeneoOptionMappingCode = BuildAkeneoOptionMappingCode(
            akeneoAttributeCode,
            akeneoOptionCode ?? optionName);

        if (!string.IsNullOrWhiteSpace(akeneoOptionMappingCode))
        {
            var mappedOptionId = await entityMappingService.GetMappedNopEntityIdByAkeneoCodeAsync(
                AkeneoEntityType.Option,
                akeneoOptionMappingCode,
                NopEntityType.SpecificationAttributeOption);

            if (mappedOptionId.HasValue)
            {
                var mappedOption = await specificationAttributeService
                    .GetSpecificationAttributeOptionByIdAsync(mappedOptionId.Value);

                if (mappedOption != null &&
                    mappedOption.SpecificationAttributeId == specificationAttributeId)
                {
                    return mappedOption;
                }
            }
        }

        var existingOptions = await specificationAttributeService
            .GetSpecificationAttributeOptionsBySpecificationAttributeAsync(specificationAttributeId);

        var existingOption = existingOptions.FirstOrDefault(option =>
            string.Equals(option.Name, optionName, StringComparison.OrdinalIgnoreCase));

        if (existingOption != null)
        {
            if (!string.IsNullOrWhiteSpace(akeneoOptionMappingCode))
            {
                await entityMappingService.UpsertAkeneoNopEntityMappingAsync(
                    AkeneoEntityType.Option,
                    akeneoOptionMappingCode,
                    null,
                    NopEntityType.SpecificationAttributeOption,
                    existingOption.Id);
            }

            return existingOption;
        }

        if (!createMissing)
            return null;

        var newOption = new SpecificationAttributeOption
        {
            SpecificationAttributeId = specificationAttributeId,
            Name = optionName ?? akeneoOptionCode,
            DisplayOrder = 0
        };

        await specificationAttributeService.InsertSpecificationAttributeOptionAsync(newOption);

        if (!string.IsNullOrWhiteSpace(akeneoOptionMappingCode))
        {
            await entityMappingService.UpsertAkeneoNopEntityMappingAsync(
                AkeneoEntityType.Option,
                akeneoOptionMappingCode,
                null,
                NopEntityType.SpecificationAttributeOption,
                newOption.Id);
        }

        return newOption;
    }

    private async Task<bool> ApplyProductAttributesAsync(
    Product product,
    IList<ResolvedMappedValue> mappedValues,
    AkeneoProductImportRequest request,
    AkeneoProductImportResult result)
    {
        var changed = false;
        var akeneoProductKey = result.AkeneoProductUuid ?? result.AkeneoIdentifier;

        foreach (var mappedValue in mappedValues.Where(value => value.TargetType == NopTargetType.ProductAttribute))
        {
            var productAttributeId = mappedValue.Mapping.NopTargetEntityId ?? 0;

            if (productAttributeId <= 0)
            {
                result.AddWarning(
                    $"Product attribute mapping has no nopCommerce product attribute. Akeneo attribute: {mappedValue.Mapping.AkeneoAttributeCode}");

                continue;
            }

            var mappingResult = await GetOrCreateProductAttributeMappingAsync(
                product.Id,
                productAttributeId,
                mappedValue.Mapping,
                akeneoProductKey);

            if (mappingResult.Changed)
            {
                changed = true;
                result.AddMessage($"Added product attribute mapping for product attribute {productAttributeId}.");
            }

            foreach (var optionItem in GetResolvedOptionItems(mappedValue))
            {
                var valueResult = await GetOrCreateProductAttributeValueAsync(
                    mappingResult.Mapping.Id,
                    mappedValue.Mapping.AkeneoAttributeCode,
                    akeneoProductKey,
                    optionItem.AkeneoOptionCode,
                    optionItem.DisplayName,
                    request.CreateMissingProductAttributeValues);

                if (valueResult.Value == null)
                {
                    result.AddWarning(
                        $"Product attribute value '{optionItem.DisplayName}' was not found and could not be created. Akeneo attribute: {mappedValue.Mapping.AkeneoAttributeCode}");

                    continue;
                }

                if (!valueResult.Changed)
                    continue;

                changed = true;
                result.AddMessage(
                    $"Added or updated product attribute value '{valueResult.Value.Name}' for product attribute {productAttributeId}.");
            }
        }

        return changed;
    }

    private async Task<ProductAttributeMappingChangeResult> GetOrCreateProductAttributeMappingAsync(
     int productId,
     int productAttributeId,
     AkeneoAttributeMapping mapping,
     string akeneoProductKey)
    {
        var akeneoMappingCode = BuildAkeneoOptionMappingCode(
            akeneoProductKey,
            mapping.AkeneoAttributeCode);

        if (!string.IsNullOrWhiteSpace(akeneoMappingCode))
        {
            var mappedProductAttributeMappingId =
                await entityMappingService.GetMappedNopEntityIdByAkeneoCodeAsync(
                    AkeneoEntityType.Attribute,
                    akeneoMappingCode,
                    NopEntityType.ProductAttributeMapping);

            if (mappedProductAttributeMappingId.HasValue)
            {
                var existingByMappingId =
                    await productAttributeService.GetProductAttributeMappingByIdAsync(
                        mappedProductAttributeMappingId.Value);

                if (existingByMappingId != null &&
                    existingByMappingId.ProductId == productId &&
                    existingByMappingId.ProductAttributeId == productAttributeId)
                {
                    return new ProductAttributeMappingChangeResult
                    {
                        Mapping = existingByMappingId,
                        Changed = false
                    };
                }
            }
        }

        var mappings = await productAttributeService.GetProductAttributeMappingsByProductIdAsync(productId);

        var existingMapping = mappings.FirstOrDefault(item =>
            item.ProductAttributeId == productAttributeId);

        if (existingMapping != null)
        {
            if (!string.IsNullOrWhiteSpace(akeneoMappingCode))
            {
                await entityMappingService.UpsertAkeneoNopEntityMappingAsync(
                    AkeneoEntityType.Attribute,
                    akeneoMappingCode,
                    null,
                    NopEntityType.ProductAttributeMapping,
                    existingMapping.Id);
            }

            return new ProductAttributeMappingChangeResult
            {
                Mapping = existingMapping,
                Changed = false
            };
        }

        var newMapping = new ProductAttributeMapping
        {
            ProductId = productId,
            ProductAttributeId = productAttributeId,
            TextPrompt = mapping.AkeneoAttributeCode,
            IsRequired = false,
            AttributeControlTypeId = (int)AttributeControlType.DropdownList,
            DisplayOrder = 0
        };

        await productAttributeService.InsertProductAttributeMappingAsync(newMapping);

        if (!string.IsNullOrWhiteSpace(akeneoMappingCode))
        {
            await entityMappingService.UpsertAkeneoNopEntityMappingAsync(
                AkeneoEntityType.Attribute,
                akeneoMappingCode,
                null,
                NopEntityType.ProductAttributeMapping,
                newMapping.Id);
        }

        return new ProductAttributeMappingChangeResult
        {
            Mapping = newMapping,
            Changed = true
        };
    }



    private async Task<bool> ApplyCustomPropertiesAsync(
        Product product,
        IList<ResolvedMappedValue> mappedValues,
        AkeneoProductImportResult result)
    {
        var changed = false;

        foreach (var mappedValue in mappedValues.Where(value => value.TargetType == NopTargetType.CustomProperty))
        {
            var key = mappedValue.Mapping.NopTargetKey;

            if (string.IsNullOrWhiteSpace(key))
                key = mappedValue.Mapping.AkeneoAttributeCode;

            var value = ApplyTransform(
                mappedValue.Value.DisplayValue,
                mappedValue.Mapping.TransformRuleJson);

            var existingValue = await genericAttributeService.GetAttributeAsync<string>(
                product,
                key);

            if (string.Equals(existingValue ?? string.Empty, value ?? string.Empty, StringComparison.Ordinal))
                continue;

            await genericAttributeService.SaveAttributeAsync(product, key, value);

            changed = true;
            result.AddMessage($"Saved custom property '{key}'.");
        }

        return changed;
    }

    private static IReadOnlyList<string> GetRawValueItems(
        AkeneoResolvedProductValue resolvedValue)
    {
        if (resolvedValue == null || resolvedValue.RawData == null)
            return Array.Empty<string>();

        var rawData = resolvedValue.RawData.Value;
        if (rawData.ValueKind == JsonValueKind.Array)
        {
            return rawData
                .EnumerateArray()
                .Select(ConvertJsonElementToString)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        var singleValue = ConvertJsonElementToString(rawData);

        return string.IsNullOrWhiteSpace(singleValue)
            ? Array.Empty<string>()
            : new[] { singleValue.Trim() };
    }


    private static string ConvertJsonElementToString(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Object => element.GetRawText(),
            _ => null
        };
    }

    private static bool TryParseDecimal(
        string value,
        out decimal parsed)
    {
        return decimal.TryParse(
            value,
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out parsed);
    }

    private static bool TryParseBoolean(
        string value,
        out bool parsed)
    {
        if (bool.TryParse(value, out parsed))
            return true;

        if (string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "y", StringComparison.OrdinalIgnoreCase))
        {
            parsed = true;
            return true;
        }

        if (string.Equals(value, "0", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "no", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "n", StringComparison.OrdinalIgnoreCase))
        {
            parsed = false;
            return true;
        }

        parsed = false;
        return false;
    }

    private async Task<AkeneoProductImportResult> SaveLogAndReturnAsync(
        AkeneoProductImportRequest request,
        AkeneoProductImportResult result,
        string rawPayloadSnapshot)
    {

        var shouldWriteItemLog =
            result.ActionType != SyncItemActionType.Skipped;

        if (!shouldWriteItemLog)
            return result;

        await syncItemLogService.InsertAkeneoSyncItemLogAsync(
            new AkeneoSyncItemLog
            {
                SyncRunRecordId = request.SyncRunRecordId,
                AkeneoProductUuid = result.AkeneoProductUuid ?? request.AkeneoProductUuid,
                AkeneoIdentifier = result.AkeneoIdentifier ?? result.Sku,
                NopProductId = result.NopProductId,
                ActionTypeId = (int)result.ActionType,
                Message = BuildLogMessage(result),
                RawPayloadSnapshot = rawPayloadSnapshot,
                CreatedOnUtc = DateTime.UtcNow
            });

        return result;
    }

    private static string BuildLogMessage(
        AkeneoProductImportResult result)
    {
        var parts = new List<string>();

        if (result.Messages.Any())
            parts.Add("Messages: " + string.Join(" | ", result.Messages));

        if (result.Warnings.Any())
            parts.Add("Warnings: " + string.Join(" | ", result.Warnings));

        if (result.Errors.Any())
            parts.Add("Errors: " + string.Join(" | ", result.Errors));

        return string.Join(Environment.NewLine, parts).Truncate(4000);
    }



    private static string ApplyTransform(
        string value,
        string transformRuleJson)
    {
        // Hook for transform JSON 
        return value;
    }

    private static IReadOnlyList<ResolvedOptionItem> GetResolvedOptionItems(ResolvedMappedValue mappedValue)
    {
        var value = mappedValue.Value;
        if (value == null)
            return Array.Empty<ResolvedOptionItem>();

        // Codes and labels must be read in the same source order (no pre-distinct) so the
        // index pairing is valid; dedupe only AFTER pairing.
        var codes = ExtractRawValuesInOrder(value);
        var labels = value.DisplayValues is { Count: > 0 }
            ? value.DisplayValues
            : (string.IsNullOrWhiteSpace(value.DisplayValue)
                ? Array.Empty<string>()
                : new[] { value.DisplayValue });

        var maxCount = Math.Max(codes.Count, labels.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<ResolvedOptionItem>();

        for (var i = 0; i < maxCount; i++)
        {
            var code = i < codes.Count ? codes[i]?.Trim() : null;
            var label = i < labels.Count ? labels[i]?.Trim() : null;
            var displayName = !string.IsNullOrWhiteSpace(label) ? label : code;

            if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(displayName))
                continue;

            var dedupeKey = code ?? displayName;
            if (!seen.Add(dedupeKey))
                continue;

            items.Add(new ResolvedOptionItem
            {
                AkeneoOptionCode = code,
                DisplayName = displayName
            });
        }

        return items;
    }

    private static IReadOnlyList<string> ExtractRawValuesInOrder(AkeneoResolvedProductValue value)
    {
        if (value?.RawData == null)
            return Array.Empty<string>();

        var rawData = value.RawData.Value;
        if (rawData.ValueKind == JsonValueKind.Array)
        {
            return rawData.EnumerateArray()
                .Select(ConvertJsonElementToString)
                .Select(v => v?.Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToList();
        }

        var single = ConvertJsonElementToString(rawData)?.Trim();
        return string.IsNullOrWhiteSpace(single) ? Array.Empty<string>() : new[] { single };
    }

    private static string BuildAkeneoOptionMappingCode(
        string akeneoAttributeCode,
        string akeneoOptionCode)
    {
        if (string.IsNullOrWhiteSpace(akeneoAttributeCode) ||
            string.IsNullOrWhiteSpace(akeneoOptionCode))
        {
            return null;
        }

        return $"{akeneoAttributeCode.Trim()}:{akeneoOptionCode.Trim()}";
    }



    private static string BuildProductAttributeValueMappingCode(
        string akeneoProductKey,
        string akeneoAttributeCode,
        string akeneoOptionCodeOrValue)
    {
        if (string.IsNullOrWhiteSpace(akeneoProductKey) ||
            string.IsNullOrWhiteSpace(akeneoAttributeCode) ||
            string.IsNullOrWhiteSpace(akeneoOptionCodeOrValue))
        {
            return null;
        }

        return $"{akeneoProductKey.Trim()}:{akeneoAttributeCode.Trim()}:{akeneoOptionCodeOrValue.Trim()}";
    }

    private sealed class ResolvedMappedValue
    {
        public AkeneoAttributeMapping Mapping { get; set; }

        public NopTargetType TargetType { get; set; }

        public AkeneoResolvedProductValue Value { get; set; }
    }


    private sealed class ResolvedOptionItem
    {
        public string AkeneoOptionCode { get; set; }

        public string DisplayName { get; set; }
    }


    private async Task<AkeneoVariantImportContext> BuildVariantImportContextAsync(
        AkeneoProductDefinition akeneoProduct,
        string akeneoIdentifier,
        string familyCode,
        AkeneoProductImportRequest request,
        AkeneoProductImportResult result,
        string sku)                        
    {
        var context = new AkeneoVariantImportContext
        {
            AkeneoIdentifier = akeneoIdentifier,
            AkeneoFamilyCode = familyCode,
            Sku = sku,
            StockQuantity = 0,
            SourceProduct = akeneoProduct,
            Locale = request.Locale,
            Channel = request.Channel,
            Currency = request.Currency
        };

        if (productValueResolver.TryGetValue(akeneoProduct, "price", out var priceValue,
                request.Locale, request.Channel, request.Currency) &&
            decimal.TryParse(priceValue.DisplayValue, out var price))
        {
            context.Price = price;
        }

        var familyConfig = await familyMappingService.GetByFamilyCodeAsync(familyCode);
        if (familyConfig == null)
            return context; // resolver will fall back to existing-nop-parent-structure detection

        var axisMappings = await familyMappingService.GetAxisMappingsAsync(familyConfig.Id);

        foreach (var axis in axisMappings)
        {
            if (productValueResolver.TryGetValue(akeneoProduct, axis.AkeneoAttributeCode, out var axisResolved,
                    request.Locale, request.Channel, request.Currency) &&
                !string.IsNullOrWhiteSpace(axisResolved.DisplayValue))
            {
                context.AxisValuesByAkeneoCode[axis.AkeneoAttributeCode] = axisResolved.DisplayValue;
            }
        }

        return context;
    }
    private static void ApplyItemResultToBatchResult(
        AkeneoProductBatchImportResult batchResult,
        AkeneoProductImportResult itemResult)
    {
        if (itemResult.Errors.Any() ||
            itemResult.ActionType == SyncItemActionType.Failed)
        {
            batchResult.FailedCount++;
            return;
        }

        if (itemResult.Warnings.Any())
            batchResult.WarningCount++;

        switch (itemResult.ActionType)
        {
            case SyncItemActionType.Created:
                batchResult.CreatedCount++;
                break;

            case SyncItemActionType.Updated:
                batchResult.UpdatedCount++;
                break;

            case SyncItemActionType.Skipped:
            default:
                batchResult.SkippedCount++;

                if (batchResult.SkippedSkuSample.Count < 50)
                {
                    var sku = itemResult.Sku ??
                              itemResult.AkeneoIdentifier ??
                              itemResult.AkeneoProductUuid;

                    if (!string.IsNullOrWhiteSpace(sku))
                        batchResult.SkippedSkuSample.Add(sku);
                }

                break;
        }
    }

    private async Task<Product> ResolveOrCreateParentProductAsync(
        string parentCode,
        AkeneoProductImportRequest request,
        AkeneoProductImportResult result,
        IDictionary<string, Product> parentProductCache,
        IDictionary<string, AkeneoProductDefinition> subModelDefinitionCache)
    {
        //TODO RE-EVALUATE POSSIBLY?
        if (parentProductCache.TryGetValue(parentCode, out var cached))
        {
            var fresh = await productService.GetProductByIdAsync(cached.Id);
            if (fresh != null)
                return fresh;

            // Cached parent was rolled back by a failed sibling's unit transaction — re-resolve.
            parentProductCache.Remove(parentCode);
        }

        var mappedParentId = await entityMappingService.GetMappedNopEntityIdByAkeneoCodeAsync(
            AkeneoEntityType.ProductModel, parentCode, NopEntityType.Product);

        if (mappedParentId.HasValue)
        {
            var existingParent = await productService.GetProductByIdAsync(mappedParentId.Value);
            if (existingParent != null)
            {
                parentProductCache[parentCode] = existingParent;
                return existingParent;
            }

            result.AddWarning($"Product model mapping exists for '{parentCode}' but nopCommerce product ID {mappedParentId.Value} was not found. Re-creating.");
        }

        var productModel = await akeneoApiClient.GetProductModelByCodeAsync(parentCode);
        if (productModel == null)
        {
            result.AddError($"Akeneo product model '{parentCode}' was not found.");
            return null;
        }

        // Product models don't have a UUID — everything keys off `code` here.
        var parentMappings = await attributeMappingService.GetAllAkeneoAttributeMappingsAsync();
        var parentMappedValues = ResolveMappedValues(productModel, parentMappings, request, result);

        var parentSku = ResolveSku(productModel, parentMappedValues);

        var parentProduct = await productService.GetProductBySkuAsync(parentSku)
            ?? CreateBaseProduct(parentSku, parentCode);

        var isNewParent = parentProduct.Id == 0;

        ApplyProductFields(parentProduct, parentMappedValues, result);

        if (isNewParent)
        {
            parentProduct.CreatedOnUtc = DateTime.UtcNow;
            parentProduct.UpdatedOnUtc = DateTime.UtcNow;
            await productService.InsertProductAsync(parentProduct);
        }
        else
        {
            parentProduct.UpdatedOnUtc = DateTime.UtcNow;
            await productService.UpdateProductAsync(parentProduct);
        }

        await entityMappingService.UpsertAkeneoNopEntityMappingAsync(
            AkeneoEntityType.ProductModel, parentCode, null, NopEntityType.Product, parentProduct.Id);

        if (request.AddMappedCategories)
            await ApplyRootCategoryMappingsAsync(parentProduct, productModel, result);

        parentProductCache[parentCode] = parentProduct;
        return parentProduct;
    }

    private async Task SaveBatchItemLogIfNeededAsync(
        AkeneoProductBatchImportRequest request,
        AkeneoProductImportResult result,
        string rawPayloadSnapshot)
    {
        var shouldWriteItemLog =
            result.ActionType == SyncItemActionType.Created ||
            result.ActionType == SyncItemActionType.Updated ||
            result.ActionType == SyncItemActionType.Failed ||
            result.Errors.Any();

        if (!shouldWriteItemLog)
            return;

        await syncItemLogService.InsertAkeneoSyncItemLogAsync(
            new AkeneoSyncItemLog
            {
                SyncRunRecordId = request.SyncRunRecordId,
                AkeneoProductUuid = result.AkeneoProductUuid ?? request.AkeneoProductUuid,
                AkeneoIdentifier = result.AkeneoIdentifier ?? result.Sku,
                NopProductId = result.NopProductId,
                ActionTypeId = (int)result.ActionType,
                Message = BuildLogMessage(result),
                RawPayloadSnapshot = rawPayloadSnapshot,
                CreatedOnUtc = DateTime.UtcNow
            });
    }


    private static bool ShouldKeepItemResultInMemory(
        AkeneoProductImportResult result)
    {
        return result.ActionType == SyncItemActionType.Created ||
               result.ActionType == SyncItemActionType.Updated ||
               result.ActionType == SyncItemActionType.Failed ||
               result.Errors.Any() ||
               result.Warnings.Any();
    }

    private static TransactionScope CreateUnitTransaction() =>
        new(
            TransactionScopeOption.Required,
            new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted },
            TransactionScopeAsyncFlowOption.Enabled);



    // Reads a single-value Akeneo attribute (e.g. an axis option code) off a product-model's `values`.
    private static string GetProductModelAttributeValue(JsonElement model, string attributeCode)
    {
        if (string.IsNullOrWhiteSpace(attributeCode))
            return null;

        if (!model.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Object)
            return null;

        if (!values.TryGetProperty(attributeCode, out var entries) || entries.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var entry in entries.EnumerateArray())
        {
            if (!entry.TryGetProperty("data", out var data))
                continue;

            return data.ValueKind switch
            {
                JsonValueKind.String => data.GetString(),
                JsonValueKind.Number => data.GetRawText(),
                _ => null
            };
        }

        return null;
    }

    // Returns the matching override rule if this sub-model's axis value triggers one; else null.
    private async Task<AkeneoFamilySubModelRule> ResolveSubModelOverrideAsync(
        string familyCode,
        AkeneoProductDefinition leaf,
        AkeneoProductDefinition subModel)
    {
        var family = await familyMappingService.GetByFamilyCodeAsync(familyCode);
        if (family is not { Enabled: true })
            return null;

        var rules = await familyMappingService.GetSubModelRulesAsync(family.Id);

        return rules.FirstOrDefault(rule => RuleMatches(rule, leaf, subModel));
    }

    private static bool RuleMatches(AkeneoFamilySubModelRule rule, AkeneoProductDefinition leaf, AkeneoProductDefinition subModel)
    {
        var hasSubModelCondition = !string.IsNullOrWhiteSpace(rule.AkeneoAxisAttributeCode);
        var hasVariantCondition = !string.IsNullOrWhiteSpace(rule.VariantAxisAttributeCode);

        // No conditions => matches nothing (guards against flattening a whole family by accident).
        if (!hasSubModelCondition && !hasVariantCondition)
            return false;

        if (hasSubModelCondition &&
            !ValueMatches(GetAkeneoAttributeValue(subModel, rule.AkeneoAxisAttributeCode), rule.TriggerValue))
            return false;

        if (hasVariantCondition &&
            !ValueMatches(GetAkeneoAttributeValue(leaf, rule.VariantAxisAttributeCode), rule.VariantTriggerValue))
            return false;

        return true;
    }

    private static bool ValueMatches(string actual, string trigger) =>
        !string.IsNullOrWhiteSpace(actual) &&
        string.Equals(actual.Trim(), trigger?.Trim(), StringComparison.OrdinalIgnoreCase);



    // Produces a new item whose root fields are the leaf's (identity untouched) and whose
    // `values` are the leaf's plus any ancestor attributes the leaf/sub-model didn't already carry.
    private static JsonElement MergeAkeneoValues(JsonElement leaf, IReadOnlyList<JsonElement> ancestorsNearestFirst)
    {
        var root = JsonNode.Parse(leaf.GetRawText())!.AsObject();

        if (root["values"] is not JsonObject mergedValues)
        {
            mergedValues = new JsonObject();
            root["values"] = mergedValues;
        }

        foreach (var ancestor in ancestorsNearestFirst)
        {
            if (!ancestor.TryGetProperty("values", out var ancestorValues) ||
                ancestorValues.ValueKind != JsonValueKind.Object)
                continue;

            foreach (var prop in ancestorValues.EnumerateObject())
            {
                if (mergedValues.ContainsKey(prop.Name))
                    continue; // leaf-most / nearer ancestor wins

                mergedValues[prop.Name] = JsonNode.Parse(prop.Value.GetRawText());
            }
        }

        return JsonSerializer.SerializeToElement(root);
    }


    private static string GetAkeneoAttributeValue(AkeneoProductDefinition item, string attributeCode)
    {
        if (string.IsNullOrWhiteSpace(attributeCode) ||
            item?.Values.ValueKind != JsonValueKind.Object)
            return null;

        if (!item.Values.TryGetProperty(attributeCode, out var entries) ||
            entries.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var entry in entries.EnumerateArray())
        {
            if (!entry.TryGetProperty("data", out var data))
                continue;

            return data.ValueKind switch
            {
                JsonValueKind.String => data.GetString(),
                JsonValueKind.Number => data.GetRawText(),
                _ => null
            };
        }

        return null;
    }

    private async Task<AkeneoProductDefinition> BuildFlattenedLeafAsync(
        AkeneoProductDefinition leaf,
        AkeneoProductDefinition subModel,
        IDictionary<string, AkeneoProductDefinition> subModelDefinitionCache)
    {
        var ancestors = new List<AkeneoProductDefinition> { subModel };

        var rootCode = subModel.Parent?.Trim();
        if (!string.IsNullOrWhiteSpace(rootCode))
        {
            var root = await GetProductModelCachedAsync(rootCode, subModelDefinitionCache);
            if (root != null)
                ancestors.Add(root);
        }

        return WithMergedValues(leaf, MergeAkeneoValues(leaf.Values, ancestors));
    }

    // Leaf values win; nearer ancestor beats farther. Only fills attributes the leaf didn't carry.
    private static JsonElement MergeAkeneoValues(
        JsonElement leafValues,
        IReadOnlyList<AkeneoProductDefinition> ancestorsNearestFirst)
    {
        var merged = leafValues.ValueKind == JsonValueKind.Object
            ? JsonNode.Parse(leafValues.GetRawText())!.AsObject()
            : new JsonObject();

        foreach (var ancestor in ancestorsNearestFirst)
        {
            if (ancestor?.Values.ValueKind != JsonValueKind.Object)
                continue;

            foreach (var prop in ancestor.Values.EnumerateObject())
            {
                if (!merged.ContainsKey(prop.Name))
                    merged[prop.Name] = JsonNode.Parse(prop.Value.GetRawText());
            }
        }

        return JsonSerializer.SerializeToElement(merged);
    }


    private async Task<ResolvedLeaf> ResolveLeafAsync(
        AkeneoProductDefinition akeneoProduct,
        AkeneoProductImportRequest request,
        AkeneoProductImportResult result)
    {
        var mappings = await attributeMappingService.GetAllAkeneoAttributeMappingsAsync();
        var mappedValues = ResolveMappedValues(akeneoProduct, mappings, request, result);

        // Required-attribute errors now land in the real result; caller checks result.Success.
        if (!result.Success)
            return new ResolvedLeaf { MappedValues = mappedValues, Sku = null, ExistingProduct = null };

        var sku = ResolveSku(akeneoProduct, mappedValues);
        result.Sku = sku;

        var existing = await ResolveNopProductAsync(
            akeneoProduct.Uuid?.Trim(),
            akeneoProduct.Identifier?.Trim(),
            sku,
            result);   // "matched by SKU" / "stale mapping" messages captured once, here

        return new ResolvedLeaf { MappedValues = mappedValues, Sku = sku, ExistingProduct = existing };
    }


    private static AkeneoProductDefinition WithMergedValues(AkeneoProductDefinition leaf, JsonElement mergedValues) => new()
    {
        Uuid = leaf.Uuid,
        Identifier = leaf.Identifier,
        Code = leaf.Code,
        Family = leaf.Family,
        FamilyVariant = leaf.FamilyVariant,
        Parent = leaf.Parent,
        Enabled = leaf.Enabled,
        Categories = leaf.Categories,
        Values = mergedValues
    };

    private sealed class ProductAttributeMappingChangeResult
    {
        public ProductAttributeMapping Mapping { get; set; }

        public bool Changed { get; set; }
    }

    private sealed class ProductAttributeValueChangeResult
    {
        public ProductAttributeValue Value { get; set; }

        public bool Changed { get; set; }
    }

    private sealed class ResolvedLeaf
    {
        public IList<ResolvedMappedValue> MappedValues { get; init; }
        public string Sku { get; init; }
        public Product ExistingProduct { get; init; }   // null => new
    }
}