using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
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

public class AkeneoProductBatchSyncService(
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
    IRepository<ProductAttributeValue> productAttributeValueRepository,
    IAkeneoProductSyncService productSyncService)
    : IAkeneoProductBatchSyncService
{

    public async Task<AkeneoProductBatchImportResult> SyncProductsAsync(
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


    public async Task<AkeneoProductImportResult> SyncProductByUuidAsync(
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
            var syncContext = await productSyncService.PrepareAsync(
                akeneoProduct,
                AkeneoEntityType.Product,
                request,
                result);

            await productSyncService.SynchronizeAsync(
                syncContext);
            return result;
        }

        var familyCode = akeneoProduct.Family?.Trim();

        // Resolve the leaf ONCE for the whole variant path.
        var leafContext = await productSyncService.PrepareAsync(
            akeneoProduct,
            AkeneoEntityType.Product,
            request,
            result );
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
                    var existingMode = await ClassifyExistingLeafStructureAsync(leafContext.ExistingProduct, leafContext.Sku);

                    if (existingMode is null or AkeneoVariantRelationshipMode.None)
                    {
                        var standaloneContext = leafContext;

                        if (overrideRule.MergeAncestorValues)
                        {
                            var flattenedLeaf = await BuildFlattenedLeafAsync(
                                akeneoProduct, subModel, subModelDefinitionCache);

                            standaloneContext = await productSyncService.PrepareAsync(
                                flattenedLeaf, AkeneoEntityType.Product, request, result);
                        }

                        await productSyncService.SynchronizeAsync(standaloneContext);
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
            akeneoProduct, akeneoIdentifier, familyCode, request, result, leafContext.Sku);
        if (!result.Success)
        {
            result.ActionType = SyncItemActionType.Failed;
            return result;
        }

        context.VariantRelationshipModeOverride = forcedMode;

        AkeneoVariantSyncResult variantResult;

        try
        {
            variantResult = await variantRelationshipService.ApplyAsync(
                parentProduct,
                context,
                upsertChildProductAsync: () =>
                    productSyncService.SynchronizeAsync(
                        leafContext));
            if (variantResult.Changed &&
                result.ActionType == SyncItemActionType.Skipped)
            {
                result.ActionType = SyncItemActionType.Updated;
            }
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

        var parentResult = new AkeneoProductImportResult
        {
            AkeneoIdentifier = parentCode,
            AkeneoProductKey = parentCode
        };

        var parentContext = await productSyncService.PrepareAsync(
            productModel,
            AkeneoEntityType.ProductModel,
            request,
            parentResult );

        var parentProduct = await productSyncService.SynchronizeAsync(
            parentContext);

        foreach (var warning in parentResult.Warnings)
        {
            result.AddWarning(
                $"Product model '{parentCode}': {warning}");
        }

        foreach (var error in parentResult.Errors)
        {
            result.AddError(
                $"Product model '{parentCode}': {error}");
        }

        foreach (var message in parentResult.Messages)
        {
            result.AddMessage(
                $"Product model '{parentCode}': {message}");
        }

        if (parentProduct == null)
            return null;

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

}