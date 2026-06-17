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

    public async Task<AkeneoProductImportResult> ImportProductByUuidAsync(
        AkeneoProductImportRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = new AkeneoProductImportResult();

        request ??= new AkeneoProductImportRequest();
        request.SyncRunId = string.IsNullOrWhiteSpace(request.SyncRunId)
            ? Guid.NewGuid().ToString("N")
            : request.SyncRunId.Trim();

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
        var akeneoProductKey =
            GetRootString(akeneoProduct, "uuid") ??
            GetRootString(akeneoProduct, "identifier");

        if (string.IsNullOrWhiteSpace(akeneoProductKey))
        {
            result.AddError("Akeneo product does not contain a uuid or identifier.");
            return result;
        }

        result.AkeneoProductKey = akeneoProductKey;

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
            akeneoProductKey,
            sku,
            result);

        var isNew = product == null;

        if (isNew && !request.CreateNewProducts)
        {
            result.AddError($"Product does not exist and CreateNewProducts is disabled. Akeneo key: {akeneoProductKey}");
            return result;
        }

        if (!isNew && !request.UpdateExistingProducts)
        {
            result.NopProductId = product.Id;
            result.AddWarning($"Product exists and UpdateExistingProducts is disabled. Product ID: {product.Id}");
            return result;
        }

        product ??= CreateBaseProduct(sku, akeneoProductKey);

        ApplyProductFields(product, mappedValues, result);

        if (isNew)
        {
            product.CreatedOnUtc = DateTime.UtcNow;
            product.UpdatedOnUtc = DateTime.UtcNow;

            await productService.InsertProductAsync(product);

            result.Created = true;
            result.AddMessage($"Created nopCommerce product ID {product.Id}.");
        }
        else
        {
            product.UpdatedOnUtc = DateTime.UtcNow;

            await productService.UpdateProductAsync(product);

            result.Updated = true;
            result.AddMessage($"Updated nopCommerce product ID {product.Id}.");
        }

        result.NopProductId = product.Id;

        await entityMappingService.UpsertAkeneoNopEntityMappingAsync(
            AkeneoEntityType.Product,
            akeneoProductKey,
            NopEntityType.Product,
            product.Id);

        await ApplySeoFieldsAsync(product, mappedValues, result);

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

        await ApplyCustomPropertiesAsync(
            product,
            mappedValues,
            result);

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
                request.Locale,
                request.Channel,
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
        string akeneoProductKey,
        string sku,
        AkeneoProductImportResult result)
    {
        var mappedProductId = await entityMappingService.GetMappedNopEntityIdAsync(
            AkeneoEntityType.Product,
            akeneoProductKey,
            NopEntityType.Product);

        if (mappedProductId.HasValue)
        {
            var mappedProduct = await productService.GetProductByIdAsync(mappedProductId.Value);

            if (mappedProduct != null)
                return mappedProduct;

            result.AddWarning(
                $"Product mapping exists for Akeneo key {akeneoProductKey}, but nopCommerce product ID {mappedProductId.Value} was not found.");
        }

        if (!string.IsNullOrWhiteSpace(sku))
        {
            var productBySku = await productService.GetProductBySkuAsync(sku);

            if (productBySku != null)
                return productBySku;
        }

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

    private static void ApplyProductFields(
        Product product,
        IList<ResolvedMappedValue> mappedValues,
        AkeneoProductImportResult result)
    {
        foreach (var mappedValue in mappedValues.Where(value => value.TargetType == NopTargetType.ProductField))
        {
            var targetKey = mappedValue.Mapping.NopTargetKey?.Trim();
            var value = ApplyTransform(mappedValue.Value.DisplayValue, mappedValue.Mapping.TransformRuleJson);

            if (string.IsNullOrWhiteSpace(targetKey))
            {
                result.AddWarning($"Product field mapping has no target key. Akeneo attribute: {mappedValue.Mapping.AkeneoAttributeCode}");
                continue;
            }

            switch (targetKey)
            {
                case "Name":
                    product.Name = value;
                    break;

                case "ShortDescription":
                    product.ShortDescription = value;
                    break;

                case "FullDescription":
                    product.FullDescription = value;
                    break;

                case "Sku":
                    product.Sku = value;
                    break;

                case "Price":
                    if (TryParseDecimal(value, out var price))
                        product.Price = price;
                    else
                        result.AddWarning($"Could not parse price value '{value}' from {mappedValue.Mapping.AkeneoAttributeCode}.");
                    break;

                case "Gtin":
                    product.Gtin = value;
                    break;

                case "ManufacturerPartNumber":
                    product.ManufacturerPartNumber = value;
                    break;

                case "Published":
                    if (TryParseBoolean(value, out var published))
                        product.Published = published;
                    else
                        result.AddWarning($"Could not parse published value '{value}' from {mappedValue.Mapping.AkeneoAttributeCode}.");
                    break;

                default:
                    result.AddWarning($"Unsupported product field target key: {targetKey}");
                    break;
            }
        }
    }

    private async Task ApplySeoFieldsAsync(
        Product product,
        IList<ResolvedMappedValue> mappedValues,
        AkeneoProductImportResult result)
    {
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
                    product.MetaTitle = value;
                    break;

                case "MetaDescription":
                    product.MetaDescription = value;
                    break;

                case "MetaKeywords":
                    product.MetaKeywords = value;
                    break;

                case "SeName":
                    await urlRecordService.SaveSlugAsync(product, value, 0);
                    break;

                default:
                    result.AddWarning($"Unsupported SEO field target key: {targetKey}");
                    break;
            }
        }

        await productService.UpdateProductAsync(product);
    }

    private async Task ApplyRootCategoryMappingsAsync(
        Product product,
        JsonElement akeneoProduct,
        AkeneoProductImportResult result)
    {
        var categoryCodes = GetRootStringArray(akeneoProduct, "categories");

        foreach (var categoryCode in categoryCodes)
        {
            var nopCategoryId = await entityMappingService.GetMappedNopEntityIdAsync(
                AkeneoEntityType.Category,
                categoryCode,
                NopEntityType.Category);

            if (!nopCategoryId.HasValue)
            {
                result.AddWarning($"No nopCommerce category mapping found for Akeneo category '{categoryCode}'.");
                continue;
            }

            await AddProductCategoryIfMissingAsync(product.Id, nopCategoryId.Value);
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
                    NopEntityType.Category);

                if (!nopCategoryId.HasValue)
                {
                    result.AddWarning(
                        $"No nopCommerce category mapping found for Akeneo category value '{categoryCode}' from attribute {mappedValue.Mapping.AkeneoAttributeCode}.");

                    continue;
                }

                await AddProductCategoryIfMissingAsync(product.Id, nopCategoryId.Value);
                result.AddMessage($"Assigned category {nopCategoryId.Value} from attribute {mappedValue.Mapping.AkeneoAttributeCode}.");
            }
        }
    }

    private async Task AddProductCategoryIfMissingAsync(
        int productId,
        int categoryId)
    {
        var existingCategories = await categoryService.GetProductCategoriesByProductIdAsync(
            productId,
            showHidden: true);

        if (existingCategories.Any(category => category.CategoryId == categoryId))
            return;

        await categoryService.InsertProductCategoryAsync(new ProductCategory
        {
            ProductId = productId,
            CategoryId = categoryId,
            DisplayOrder = 0
        });
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
                result.AddWarning($"Specification attribute mapping has no nopCommerce specification attribute. Akeneo attribute: {mappedValue.Mapping.AkeneoAttributeCode}");
                continue;
            }

            foreach (var value in GetDisplayValueItems(mappedValue.Value))
            {
                var option = await GetOrCreateSpecificationAttributeOptionAsync(
                    specificationAttributeId,
                    value,
                    request.CreateMissingSpecificationAttributeOptions);

                if (option == null)
                {
                    result.AddWarning($"Specification option '{value}' was not found and could not be created.");
                    continue;
                }

                await AddProductSpecificationAttributeIfMissingAsync(product.Id, option.Id);
                result.AddMessage($"Assigned specification option '{value}' to product.");
            }
        }
    }

    private async Task<SpecificationAttributeOption> GetOrCreateSpecificationAttributeOptionAsync(
        int specificationAttributeId,
        string optionName,
        bool createMissing)
    {
        if (string.IsNullOrWhiteSpace(optionName))
            return null;

        optionName = optionName.Trim();

        var existingOptions = await specificationAttributeService
            .GetSpecificationAttributeOptionsBySpecificationAttributeAsync(specificationAttributeId);

        var existingOption = existingOptions.FirstOrDefault(option =>
            string.Equals(option.Name, optionName, StringComparison.OrdinalIgnoreCase));

        if (existingOption != null)
            return existingOption;

        if (!createMissing)
            return null;

        var newOption = new SpecificationAttributeOption
        {
            SpecificationAttributeId = specificationAttributeId,
            Name = optionName,
            DisplayOrder = 0
        };

        await specificationAttributeService.InsertSpecificationAttributeOptionAsync(newOption);

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
        foreach (var mappedValue in mappedValues.Where(value => value.TargetType == NopTargetType.ProductAttribute))
        {
            var productAttributeId = mappedValue.Mapping.NopTargetEntityId ?? 0;

            if (productAttributeId <= 0)
            {
                result.AddWarning($"Product attribute mapping has no nopCommerce product attribute. Akeneo attribute: {mappedValue.Mapping.AkeneoAttributeCode}");
                continue;
            }

            var productAttributeMapping = await GetOrCreateProductAttributeMappingAsync(
                product.Id,
                productAttributeId,
                mappedValue.Mapping.AkeneoAttributeCode);

            foreach (var value in GetDisplayValueItems(mappedValue.Value))
            {
                await AddProductAttributeValueIfMissingAsync(
                    productAttributeMapping.Id,
                    value,
                    request.CreateMissingProductAttributeValues);

                result.AddMessage($"Added product attribute value '{value}' to product attribute {productAttributeId}.");
            }
        }
    }

    private async Task<ProductAttributeMapping> GetOrCreateProductAttributeMappingAsync(
        int productId,
        int productAttributeId,
        string textPrompt)
    {
        var mappings = await productAttributeService.GetProductAttributeMappingsByProductIdAsync(productId);

        var existingMapping = mappings.FirstOrDefault(mapping =>
            mapping.ProductAttributeId == productAttributeId);

        if (existingMapping != null)
            return existingMapping;

        var newMapping = new ProductAttributeMapping
        {
            ProductId = productId,
            ProductAttributeId = productAttributeId,
            TextPrompt = textPrompt,
            IsRequired = false,
            AttributeControlTypeId = (int)AttributeControlType.DropdownList,
            DisplayOrder = 0
        };

        await productAttributeService.InsertProductAttributeMappingAsync(newMapping);

        return newMapping;
    }

    private async Task AddProductAttributeValueIfMissingAsync(
        int productAttributeMappingId,
        string valueName,
        bool createMissing)
    {
        if (string.IsNullOrWhiteSpace(valueName))
            return;

        valueName = valueName.Trim();

        var existingValues = await productAttributeService.GetProductAttributeValuesAsync(
            productAttributeMappingId);

        var existingValue = existingValues.FirstOrDefault(value =>
            string.Equals(value.Name, valueName, StringComparison.OrdinalIgnoreCase));

        if (existingValue != null || !createMissing)
            return;

        await productAttributeService.InsertProductAttributeValueAsync(
            new ProductAttributeValue
            {
                ProductAttributeMappingId = productAttributeMappingId,
                AttributeValueTypeId = (int)AttributeValueType.Simple,
                Name = valueName,
                DisplayOrder = 0
            });
    }

    private async Task ApplyCustomPropertiesAsync(
        Product product,
        IList<ResolvedMappedValue> mappedValues,
        AkeneoProductImportResult result)
    {
        foreach (var mappedValue in mappedValues.Where(value => value.TargetType == NopTargetType.CustomProperty))
        {
            var key = mappedValue.Mapping.NopTargetKey;

            if (string.IsNullOrWhiteSpace(key))
                key = mappedValue.Mapping.AkeneoAttributeCode;

            var value = ApplyTransform(
                mappedValue.Value.DisplayValue,
                mappedValue.Mapping.TransformRuleJson);

            await genericAttributeService.SaveAttributeAsync(product, key, value);

            result.AddMessage($"Saved custom property '{key}'.");
        }
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

    private static IReadOnlyList<string> GetDisplayValueItems(
        AkeneoResolvedProductValue resolvedValue)
    {
        if (resolvedValue == null || string.IsNullOrWhiteSpace(resolvedValue.DisplayValue) || resolvedValue.RawData == null)
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

        return new[] { resolvedValue.DisplayValue.Trim() };
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
        await syncItemLogService.InsertAkeneoSyncItemLogAsync(
            new AkeneoSyncItemLog
            {
                SyncRunId = request.SyncRunId,
                AkeneoProductUuid = result.AkeneoProductUuid ?? request.AkeneoProductUuid,
                AkeneoIdentifier = result.AkeneoIdentifier ?? result.Sku,
                NopProductId = result.NopProductId,
                ActionTypeId = (int)ResolveActionType(result),
                Message = BuildLogMessage(result),
                RawPayloadSnapshot = rawPayloadSnapshot,
                CreatedOnUtc = DateTime.UtcNow
            });

        return result;
    }

    private static ActionType ResolveActionType(
        AkeneoProductImportResult result)
    {
        if (result.Errors.Any())
            return ActionType.Failed;

        if (result.Created)
            return ActionType.Created;

        if (result.Updated)
            return ActionType.Updated;

        return ActionType.Skipped;
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

    private sealed class ResolvedMappedValue
    {
        public AkeneoAttributeMapping Mapping { get; set; }

        public NopTargetType TargetType { get; set; }

        public AkeneoResolvedProductValue Value { get; set; }
    }
}