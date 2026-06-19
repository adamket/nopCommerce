using System.Globalization;
using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Nop.Core.Domain.Catalog;
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
    IUrlRecordService urlRecordService)
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

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var page = await akeneoApiClient.GetProductsPageAsync(
                    pageSize,
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
                        AkeneoProductUuid = GetRootString(akeneoProduct, "uuid"),
                        AkeneoIdentifier = GetRootString(akeneoProduct, "identifier"),
                        AkeneoProductKey =
                            GetRootString(akeneoProduct, "uuid") ??
                            GetRootString(akeneoProduct, "identifier")
                    };

                    string rawPayloadSnapshot = null;

                    try
                    {
                        if (request.SaveRawPayloadSnapshot)
                        {
                            rawPayloadSnapshot = TruncateForLog(
                                akeneoProduct.GetRawText(),
                                12000);
                        }

                        itemResult = await ImportProductAsync(
                            akeneoProduct,
                            request,
                            itemResult);

                        ApplyItemResultToBatchResult(
                            batchResult,
                            itemResult);

                        await SaveBatchItemLogIfNeededAsync(
                            request,
                            itemResult,
                            rawPayloadSnapshot);

                        if (ShouldKeepItemResultInMemory(itemResult))
                            batchResult.LoggedItemResults.Add(itemResult);
                    }
                    catch (Exception ex)
                    {
                        itemResult.AddError(ex.Message);
                        itemResult.ActionType = SyncItemActionType.Failed;

                        ApplyItemResultToBatchResult(
                            batchResult,
                            itemResult);

                        await SaveBatchItemLogIfNeededAsync(
                            request,
                            itemResult,
                            rawPayloadSnapshot);

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
        {
            throw new ArgumentException("SyncRunRecordId must be provided in the request.", nameof(request));
        }

        result.AkeneoProductUuid = request.AkeneoProductUuid;

        JsonElement? akeneoProduct = null;
        string rawPayloadSnapshot = null;

        try
        {
            if (string.IsNullOrWhiteSpace(request.AkeneoProductUuid))
            {
                result.AddError("Akeneo product UUID is required.");
                return await SaveLogAndReturnAsync(request, result, rawPayloadSnapshot);
            }

            akeneoProduct = await akeneoApiClient.GetProductByUuidAsync(
                request.AkeneoProductUuid,
                cancellationToken);

            if (!akeneoProduct.HasValue)
            {
                result.AddError($"Akeneo product was not found. UUID: {request.AkeneoProductUuid}");
                return await SaveLogAndReturnAsync(request, result, rawPayloadSnapshot);
            }

            result.AkeneoProductUuid = GetRootString(akeneoProduct.Value, "uuid");
            result.AkeneoIdentifier = GetRootString(akeneoProduct.Value, "identifier");

            if (request.SaveRawPayloadSnapshot)
                rawPayloadSnapshot = TruncateForLog(akeneoProduct.Value.GetRawText(), 12000);

            result = await ImportProductAsync(
                akeneoProduct.Value,
                request,
                result);

            return await SaveLogAndReturnAsync(request, result, rawPayloadSnapshot);
        }
        catch (Exception ex)
        {
            result.AddError(ex.Message);

            return await SaveLogAndReturnAsync(
                request,
                result,
                rawPayloadSnapshot);
        }
    }

    private async Task<AkeneoProductImportResult> ImportProductAsync(
        JsonElement akeneoProduct,
        AkeneoProductImportRequest request,
        AkeneoProductImportResult result)
    {
        var akeneoUuid = GetRootString(akeneoProduct, "uuid")?.Trim();
        var akeneoIdentifier = GetRootString(akeneoProduct, "identifier")?.Trim();

        if (string.IsNullOrWhiteSpace(akeneoUuid) &&
            string.IsNullOrWhiteSpace(akeneoIdentifier))
        {
            result.AddError("Akeneo product does not contain a uuid or identifier.");
            return result;
        }

        result.AkeneoProductUuid = akeneoUuid;
        result.AkeneoIdentifier = akeneoIdentifier;
        result.AkeneoProductKey = akeneoUuid ?? akeneoIdentifier;

        var mappings = await attributeMappingService.GetAllAkeneoAttributeMappingsAsync();

        var mappedValues = ResolveMappedValues(
            akeneoProduct,
            mappings,
            request,
            result);

        if (!result.Success)
            return result;

        var sku = ResolveSku(akeneoProduct, mappedValues);
        result.Sku = sku;

        var product = await ResolveNopProductAsync(
            akeneoUuid,
            akeneoIdentifier,
            sku,
            result);

        var isNew = product == null;

        if (isNew && !request.CreateNewProducts)
        {
            result.AddError(
                $"Product does not exist and CreateNewProducts is disabled. Akeneo UUID: {akeneoUuid}, identifier: {akeneoIdentifier}");

            return result;
        }

        if (!isNew && !request.UpdateExistingProducts)
        {
            result.NopProductId = product.Id;
            result.AddWarning($"Product exists and UpdateExistingProducts is disabled. Product ID: {product.Id}");
            return result;
        }

        product ??= CreateBaseProduct(
            sku,
            akeneoIdentifier ?? akeneoUuid);

        var productChanged = ApplyProductFields(
            product,
            mappedValues,
            result);

        if (isNew)
        {
            product.CreatedOnUtc = DateTime.UtcNow;
            product.UpdatedOnUtc = DateTime.UtcNow;

            await productService.InsertProductAsync(product);

            result.ActionType = SyncItemActionType.Created;
            result.AddMessage($"Created nopCommerce product ID {product.Id}.");
        }
        else if (productChanged)
        {
            product.UpdatedOnUtc = DateTime.UtcNow;

            await productService.UpdateProductAsync(product);

            result.ActionType = SyncItemActionType.Updated;
            result.AddMessage($"Updated nopCommerce product ID {product.Id}.");
        }
        else
        {
            result.ActionType = SyncItemActionType.Skipped;
            return result;
        }

        result.NopProductId = product.Id;

        await entityMappingService.UpsertAkeneoNopEntityMappingAsync(
            AkeneoEntityType.Product,
            akeneoIdentifier,
            akeneoUuid,
            NopEntityType.Product,
            product.Id);

        var seoChanged = await ApplySeoFieldsAsync(
            product,
            mappedValues,
            result);


        if (request.AddMappedCategories)
        {
            await ApplyRootCategoryMappingsAsync(
                product,
                akeneoProduct,
                result);

            await ApplyAttributeCategoryMappingsAsync(
                product,
                mappedValues,
                result);
        }

        if (request.AddMappedManufacturers)
        {
            //await ApplyManufacturerMappingsAsync(
            //    product,
            //    mappedValues,
            //    result);
        }

        await ApplySpecificationAttributesAsync(
            product,
            mappedValues,
            request,
            result);

        await ApplyProductAttributesAsync(
            product,
            mappedValues,
            request,
            result);

        var customPropertiesChanged = await ApplyCustomPropertiesAsync(
            product,
            mappedValues,
            result);

        if (customPropertiesChanged)
            result.ActionType = SyncItemActionType.Updated;

        return result;
    }

    private IList<ResolvedMappedValue> ResolveMappedValues(
        JsonElement akeneoProduct,
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

            if (!hasValue || string.IsNullOrWhiteSpace(value?.DisplayValue))
            {
                if (mapping.IsRequired)
                {
                    result.AddError(
                        $"Required Akeneo attribute is missing a value: {mapping.AkeneoAttributeCode}");
                }

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
        JsonElement akeneoProduct,
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

        return GetRootString(akeneoProduct, "identifier")?.Trim();
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
                    changed |= SetStringIfChanged(
                        product.Name,
                        value,
                        newValue => product.Name = newValue);
                    break;

                case "ShortDescription":
                    changed |= SetStringIfChanged(
                        product.ShortDescription,
                        value,
                        newValue => product.ShortDescription = newValue);
                    break;

                case "FullDescription":
                    changed |= SetStringIfChanged(
                        product.FullDescription,
                        value,
                        newValue => product.FullDescription = newValue);
                    break;

                case "Sku":
                    changed |= SetStringIfChanged(
                        product.Sku,
                        value,
                        newValue => product.Sku = newValue);
                    break;

                case "Price":
                    if (TryParseDecimal(value, out var price))
                    {
                        changed |= SetDecimalIfChanged(
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
                    changed |= SetStringIfChanged(
                        product.Gtin,
                        value,
                        newValue => product.Gtin = newValue);
                    break;

                case "ManufacturerPartNumber":
                    changed |= SetStringIfChanged(
                        product.ManufacturerPartNumber,
                        value,
                        newValue => product.ManufacturerPartNumber = newValue);
                    break;

                case "Published":
                    if (TryParseBoolean(value, out var published))
                    {
                        changed |= SetBoolIfChanged(
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
      AkeneoProductImportResult result)
    {
        var changed = false;

        foreach (var mappedValue in mappedValues.Where(value => value.TargetType == NopTargetType.SeoField))
        {
            var targetKey = mappedValue.Mapping.NopTargetKey?.Trim();
            var value = ApplyTransform(
                mappedValue.Value.DisplayValue,
                mappedValue.Mapping.TransformRuleJson);

            if (string.IsNullOrWhiteSpace(targetKey))
            {
                result.AddWarning(
                    $"SEO mapping has no target key. Akeneo attribute: {mappedValue.Mapping.AkeneoAttributeCode}");

                continue;
            }

            switch (targetKey)
            {
                case "MetaTitle":
                    changed |= SetStringIfChanged(
                        product.MetaTitle,
                        value,
                        newValue => product.MetaTitle = newValue);
                    break;

                case "MetaDescription":
                    changed |= SetStringIfChanged(
                        product.MetaDescription,
                        value,
                        newValue => product.MetaDescription = newValue);
                    break;

                case "MetaKeywords":
                    changed |= SetStringIfChanged(
                        product.MetaKeywords,
                        value,
                        newValue => product.MetaKeywords = newValue);
                    break;

                case "SeName":
                    // For now, this may still write every time depending on SaveSlugAsync behavior.
                    // Better version: compare current slug first.
                    await urlRecordService.SaveSlugAsync(product, value, 0);
                    result.AddMessage($"Saved SEO slug '{value}'.");
                    break;

                default:
                    result.AddWarning($"Unsupported SEO field target key: {targetKey}");
                    break;
            }
        }

        if (!changed) {  return false; }

        product.UpdatedOnUtc = DateTime.UtcNow;

        await productService.UpdateProductAsync(product);

        result.ActionType = SyncItemActionType.Updated;
        result.AddMessage($"Updated SEO fields for nopCommerce product ID {product.Id}.");

        return true;
    }

    private async Task ApplyRootCategoryMappingsAsync(
        Product product,
        JsonElement akeneoProduct,
        AkeneoProductImportResult result)
    {
        var categoryCodes = GetRootStringArray(akeneoProduct, "categories");

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

            if (categoryAdded)
            {
                result.ActionType = SyncItemActionType.Updated;
                result.AddMessage($"Assigned category {nopCategoryId.Value} from Akeneo category '{categoryCode}'.");
            }

            result.AddMessage($"Assigned category {nopCategoryId.Value} from Akeneo category '{categoryCode}'.");
        }
    }

    private async Task ApplyAttributeCategoryMappingsAsync(
        Product product,
        IList<ResolvedMappedValue> mappedValues,
        AkeneoProductImportResult result)
    {
        foreach (var mappedValue in mappedValues.Where(value => value.TargetType == NopTargetType.Category))
        {
            foreach (var categoryCode in GetRawValueItems(mappedValue.Value))
            {
                var nopCategoryId = await entityMappingService.GetMappedNopEntityIdAsync(
                    AkeneoEntityType.Category,
                    categoryCode,
                    null,
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

                if (categoryAdded)
                {
                    result.ActionType = SyncItemActionType.Updated;
                    result.AddMessage($"Assigned category {nopCategoryId.Value} from Akeneo category '{categoryCode}'.");
                }

                result.AddMessage($"Assigned category {nopCategoryId.Value} from attribute {mappedValue.Mapping.AkeneoAttributeCode}.");
            }
        }
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

    private async Task ApplySpecificationAttributesAsync(
        Product product,
        IList<ResolvedMappedValue> mappedValues,
        AkeneoProductImportRequest request,
        AkeneoProductImportResult result)
    {
        foreach (var mappedValue in mappedValues.Where(value => value.TargetType == NopTargetType.SpecificationAttribute))
        {
            var specificationAttributeId = mappedValue.Mapping.NopTargetEntityId ?? 0;

            if (specificationAttributeId <= 0)
            {
                result.AddWarning(
                    $"Specification attribute mapping has no nopCommerce specification attribute. Akeneo attribute: {mappedValue.Mapping.AkeneoAttributeCode}");

                continue;
            }

            var optionItems = GetResolvedOptionItems(mappedValue);

            foreach (var optionItem in optionItems)
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

                await AddProductSpecificationAttributeIfMissingAsync(
                    product.Id,
                    option.Id);

                result.AddMessage(
                    $"Assigned specification option '{option.Name}' to product.");
            }
        }
    }

    private async Task<ProductAttributeValue> GetOrCreateProductAttributeValueAsync(
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
            return null;
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
                    }

                    return mappedValue;
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

            return existingValue;
        }

        if (!createMissing)
            return null;

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

        return newValue;
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

    private async Task AddProductSpecificationAttributeIfMissingAsync(
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
            return;
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
    }

    private async Task ApplyProductAttributesAsync(
        Product product,
        IList<ResolvedMappedValue> mappedValues,
        AkeneoProductImportRequest request,
        AkeneoProductImportResult result)
    {
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

            var productAttributeMapping = await GetOrCreateProductAttributeMappingAsync(
                product.Id,
                productAttributeId,
                mappedValue.Mapping,
                akeneoProductKey);

            foreach (var optionItem in GetResolvedOptionItems(mappedValue))
            {
                var productAttributeValue = await GetOrCreateProductAttributeValueAsync(
                    productAttributeMapping.Id,
                    mappedValue.Mapping.AkeneoAttributeCode,
                    akeneoProductKey,
                    optionItem.AkeneoOptionCode,
                    optionItem.DisplayName,
                    request.CreateMissingProductAttributeValues);

                if (productAttributeValue == null)
                {
                    result.AddWarning(
                        $"Product attribute value '{optionItem.DisplayName}' was not found and could not be created. Akeneo attribute: {mappedValue.Mapping.AkeneoAttributeCode}");

                    continue;
                }

                result.AddMessage(
                    $"Added product attribute value '{productAttributeValue.Name}' to product attribute {productAttributeId}.");
            }
        }
    }

    private async Task<ProductAttributeMapping> GetOrCreateProductAttributeMappingAsync(
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
                    return existingByMappingId;
                }
            }
        }

        var mappings = await productAttributeService.GetProductAttributeMappingsByProductIdAsync(productId);

        var existingMapping = mappings.FirstOrDefault(item =>
            item.ProductAttributeId == productAttributeId);

        if (existingMapping == null)
        {
            existingMapping = new ProductAttributeMapping
            {
                ProductId = productId,
                ProductAttributeId = productAttributeId,
                TextPrompt = mapping.AkeneoAttributeCode,
                IsRequired = false,
                AttributeControlTypeId = (int)AttributeControlType.DropdownList,
                DisplayOrder = 0
            };

            await productAttributeService.InsertProductAttributeMappingAsync(existingMapping);
        }

        if (!string.IsNullOrWhiteSpace(akeneoMappingCode))
        {
            await entityMappingService.UpsertAkeneoNopEntityMappingAsync(
                AkeneoEntityType.Attribute,
                akeneoMappingCode,
                null,
                NopEntityType.ProductAttributeMapping,
                existingMapping.Id);
        }

        return existingMapping;
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

    private static IReadOnlyList<string> GetRootStringArray(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return property
            .EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string GetRootString(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return null;

        return property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
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

    private static IReadOnlyList<string> GetDisplayValueItems(AkeneoResolvedProductValue resolvedValue)
    {
        if (resolvedValue == null)
            return Array.Empty<string>();

        if (resolvedValue.DisplayValues is { Count: > 0 })
            return resolvedValue.DisplayValues;

        return string.IsNullOrWhiteSpace(resolvedValue.DisplayValue)
            ? Array.Empty<string>()
            : new[] { resolvedValue.DisplayValue.Trim() };
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

        return TruncateForLog(string.Join(Environment.NewLine, parts), 4000);
    }

    private static string TruncateForLog(
        string value,
        int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        return value.Length <= maxLength
            ? value
            : value.Substring(0, maxLength) + "...";
    }

    private static string ApplyTransform(
        string value,
        string transformRuleJson)
    {
        // Hook for your transform JSON later.
        // For the first import pass, return the resolved Akeneo value unchanged.
        return value;
    }

    private static IReadOnlyList<ResolvedOptionItem> GetResolvedOptionItems(
        ResolvedMappedValue mappedValue)
    {
        var rawItems = GetRawValueItems(mappedValue.Value);
        var displayItems = GetDisplayValueItems(mappedValue.Value);

        var maxCount = Math.Max(rawItems.Count, displayItems.Count);
        var items = new List<ResolvedOptionItem>();

        for (var i = 0; i < maxCount; i++)
        {
            var rawValue = i < rawItems.Count ? rawItems[i] : null;
            var displayValue = i < displayItems.Count ? displayItems[i] : rawValue;

            if (string.IsNullOrWhiteSpace(rawValue) &&
                string.IsNullOrWhiteSpace(displayValue))
            {
                continue;
            }

            items.Add(new ResolvedOptionItem
            {
                AkeneoOptionCode = rawValue?.Trim(),
                DisplayName = displayValue?.Trim() ?? rawValue?.Trim()
            });
        }

        return items;
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

    private static bool SetStringIfChanged(
        string currentValue,
        string newValue,
        Action<string> setter)
    {
        currentValue ??= string.Empty;
        newValue ??= string.Empty;

        if (string.Equals(currentValue, newValue, StringComparison.Ordinal))
            return false;

        setter(newValue);
        return true;
    }

    private static bool SetDecimalIfChanged(
        decimal currentValue,
        decimal newValue,
        Action<decimal> setter)
    {
        if (currentValue == newValue)
            return false;

        setter(newValue);
        return true;
    }

    private static bool SetBoolIfChanged(
        bool currentValue,
        bool newValue,
        Action<bool> setter)
    {
        if (currentValue == newValue)
            return false;

        setter(newValue);
        return true;
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

    private async Task SaveBatchItemLogIfNeededAsync(
        AkeneoProductBatchImportRequest request,
        AkeneoProductImportResult result,
        string rawPayloadSnapshot)
    {
        var shouldWriteItemLog =
            result.ActionType == SyncItemActionType.Created ||
            result.ActionType == SyncItemActionType.Updated ||
            result.ActionType == SyncItemActionType.Failed ||
            result.Errors.Any() ||
            result.Warnings.Any() ||
            request.LogSkippedProducts;

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
}