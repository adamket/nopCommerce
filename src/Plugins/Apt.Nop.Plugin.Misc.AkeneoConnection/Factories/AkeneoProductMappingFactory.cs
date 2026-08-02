using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Factories;

/// <summary>
/// Resolves the requested Akeneo source and delegates mapping/value resolution
/// to the same read-only prepare path used by the real synchronization pipeline.
/// The resulting context is converted into a dry-run change plan without
/// performing any nopCommerce writes.
/// </summary>
public sealed class AkeneoProductMappingFactory(
    IAkeneoApiClient akeneoApiClient,
    IAkeneoFamilyMappingService familyMappingService,
    IAkeneoProductModelHierarchyResolver productModelHierarchyResolver,
    IAkeneoVariantRelationshipResolver variantRelationshipResolver,
    IAkeneoLeafRepresentationClassifier leafRepresentationClassifier,
    IAkeneoProductSyncService productSyncService,
    IAkeneoProductBatchImportRequestFactory requestFactory,
    IAkeneoDryRunChangeAnalyzer changeAnalyzer)
    : IAkeneoProductMappingFactory
{
    private const string DefaultLocale = "en_US";
    private const string DefaultChannel = "ecommerce";
    private const string DefaultCurrency = "USD";

    public async Task<AkeneoProductMappingPreviewModel> PreviewProductMappingAsync(
        string akeneoIdentifier,
        string locale,
        string channel,
        string currency,
        AkeneoSyncProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        locale = locale.TrimOr(DefaultLocale);
        channel = channel.TrimOr(DefaultChannel);
        currency = currency.TrimOr(DefaultCurrency);

        var model = new AkeneoProductMappingPreviewModel
        {
            AkeneoIdentifier = akeneoIdentifier,
            Locale = locale,
            Channel = channel,
            Currency = currency,
            SyncProfileId = profile.Id,
            SyncProfileName = profile.Name,
            HasSearched = true
        };

        if (!profile.Enabled)
        {
            model.ImportAllowedByProfile = false;
            model.ImportBlockReason =
                "The default sync profile is disabled. The preview is read-only until the profile is enabled and saved.";
        }

        if (string.IsNullOrWhiteSpace(akeneoIdentifier))
        {
            model.Errors.Add(
                "Enter an Akeneo identifier/SKU or product model code.");
            return model;
        }

        akeneoIdentifier = akeneoIdentifier.Trim();

        var akeneoSource = await FindAkeneoProductByIdentifierAsync(
            akeneoIdentifier,
            cancellationToken);

        var sourceEntityType = AkeneoEntityType.Product;

        if (akeneoSource == null)
        {
            akeneoSource = await akeneoApiClient.GetProductModelByCodeAsync(
                akeneoIdentifier,
                cancellationToken);

            sourceEntityType = AkeneoEntityType.ProductModel;
        }

        if (akeneoSource == null)
        {
            model.AkeneoProductFound = false;
            model.Errors.Add(
                $"No Akeneo product or product model was found for \"{akeneoIdentifier}\".");
            return model;
        }

        model.AkeneoProductFound = true;
        model.AkeneoEntityTypeId = (int)sourceEntityType;
        model.AkeneoProductUuid = sourceEntityType == AkeneoEntityType.Product
            ? akeneoSource.Uuid
            : null;
        model.AkeneoProductModelCode = sourceEntityType == AkeneoEntityType.ProductModel
            ? akeneoSource.Code
            : null;

        if (sourceEntityType == AkeneoEntityType.Product &&
            string.IsNullOrWhiteSpace(model.AkeneoProductUuid))
        {
            model.Errors.Add(
                "The Akeneo product was found, but it did not contain a UUID. Import cannot run.");
            return model;
        }

        if (sourceEntityType == AkeneoEntityType.ProductModel &&
            string.IsNullOrWhiteSpace(model.AkeneoProductModelCode))
        {
            model.Errors.Add(
                "The Akeneo product model was found, but it did not contain a code. Import cannot run.");
            return model;
        }

        var previewSource = sourceEntityType == AkeneoEntityType.ProductModel
            ? await BuildEffectiveProductModelPreviewSourceAsync(
                akeneoSource,
                model,
                cancellationToken)
            : await BuildEffectivePreviewSourceAsync(
                akeneoSource,
                model,
                cancellationToken);

        if (sourceEntityType == AkeneoEntityType.ProductModel)
            model.AkeneoProductModelCode = previewSource.Code;

        var request = requestFactory.CreateFromProfile(
            profile,
            syncRunRecordId: 0,
            runMode: AkeneoRunMode.SingleProduct);

        request.Locale = locale;
        request.Channel = channel;
        request.Currency = currency;
        request.AkeneoProductUuid = model.AkeneoProductUuid;
        request.AkeneoProductModelCode = model.AkeneoProductModelCode;
        request.SaveRawPayloadSnapshot = false;

        var result = new AkeneoProductImportResult
        {
            AkeneoProductUuid = model.AkeneoProductUuid,
            AkeneoIdentifier = sourceEntityType == AkeneoEntityType.ProductModel
                ? model.AkeneoProductModelCode
                : previewSource.Identifier
        };

        var mappingFamilyCode = previewSource.Family ?? akeneoSource.Family;
        var mappingEntityScope =
            AkeneoAttributeMappingScopeHelper.ResolveDefaultCurrentScope(
                previewSource,
                sourceEntityType);

        var context = await productSyncService.PrepareAsync(
            previewSource,
            sourceEntityType,
            request,
            result,
            mappingFamilyCode,
            mappingEntityScope,
            cancellationToken);

        await PopulateHierarchyDecisionAsync(
            akeneoSource,
            previewSource,
            sourceEntityType,
            request,
            context,
            model,
            cancellationToken);

        AppendDistinct(model.Messages, result.Messages);
        AppendDistinct(model.Warnings, result.Warnings);
        AppendDistinct(model.Errors, result.Errors);

        if (!context.MappedValues.Any())
        {
            model.Warnings.Add(
                "No applicable mapped or configured unmapped values were resolved for this source and destination role.");
        }

        AddMappingCoverageWarnings(model, context);

        await changeAnalyzer.AnalyzeAsync(
            context,
            model,
            cancellationToken);

        model.Warnings = model.Warnings
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        model.Errors = model.Errors
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        model.Messages = model.Messages
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        model.CoverageNotes = model.CoverageNotes
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return model;
    }

    private async Task<AkeneoProductDefinition> BuildEffectivePreviewSourceAsync(
        AkeneoProductDefinition product,
        AkeneoProductMappingPreviewModel model,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(product.Parent))
        {
            model.HierarchyDecision =
                $"Akeneo product '{product.Identifier ?? product.Uuid}' is standalone and will synchronize directly to one nopCommerce product.";
            model.HierarchyRequiresReview = false;
            return product;
        }

        var ancestors = new List<AkeneoProductDefinition>();
        var visitedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var currentCode = product.Parent.Trim();
        var cycleDetected = false;

        while (!string.IsNullOrWhiteSpace(currentCode))
        {
            if (!visitedCodes.Add(currentCode))
            {
                cycleDetected = true;
                break;
            }

            cancellationToken.ThrowIfCancellationRequested();

            var productModel = await akeneoApiClient
                .GetProductModelByCodeAsync(currentCode, cancellationToken);

            if (productModel == null)
            {
                model.Warnings.Add(
                    $"Product-model inheritance could not be previewed because Akeneo product model '{currentCode}' was not found.");
                break;
            }

            ancestors.Add(productModel);
            currentCode = productModel.Parent?.Trim();
        }

        if (ancestors.Count == 0)
        {
            model.ImmediateParentProductModelCode = product.Parent?.Trim();
            model.EffectiveParentProductModelCode = product.Parent?.Trim();
            model.HierarchyDecision =
                $"Akeneo product '{product.Identifier ?? product.Uuid}' references parent model '{product.Parent}', but the model hierarchy could not be resolved completely.";
            model.HierarchyRequiresReview = true;
            return product;
        }

        if (cycleDetected)
        {
            model.Warnings.Add(
                $"A product-model inheritance cycle was detected at '{currentCode}'. Preview used the values resolved before the cycle.");
        }

        var familyMapping = await familyMappingService
            .GetByFamilyCodeAsync(product.Family);

        var hierarchyMode = familyMapping is { Enabled: true }
            ? familyMapping.ProductModelHierarchyMode
            : AkeneoProductModelHierarchyMode.ImmediateParentProductModel;

        var hierarchy = productModelHierarchyResolver.Resolve(
            product,
            ancestors,
            hierarchyMode);

        model.ImmediateParentProductModelCode =
            hierarchy.ImmediateParentModel?.Code ?? product.Parent?.Trim();
        model.EffectiveParentProductModelCode =
            hierarchy.EffectiveParentProductModel?.Code ?? product.Parent?.Trim();
        model.HierarchyDecision =
            $"Akeneo leaf '{product.Identifier ?? product.Uuid}' uses {FormatHierarchyMode(hierarchy.Mode)}: " +
            $"immediate parent '{model.ImmediateParentProductModelCode}', effective nopCommerce parent '{model.EffectiveParentProductModelCode}'.";

        return hierarchy.EffectiveLeaf;
    }

    private async Task<AkeneoProductDefinition>
        BuildEffectiveProductModelPreviewSourceAsync(
            AkeneoProductDefinition productModel,
            AkeneoProductMappingPreviewModel model,
            CancellationToken cancellationToken)
    {
        var selectedCode = productModel.Code?.Trim();
        if (string.IsNullOrWhiteSpace(selectedCode))
            return productModel;

        var ancestors = new List<AkeneoProductDefinition>();
        var visitedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            selectedCode
        };
        var currentCode = productModel.Parent?.Trim();
        var cycleDetected = false;

        while (!string.IsNullOrWhiteSpace(currentCode))
        {
            if (!visitedCodes.Add(currentCode))
            {
                cycleDetected = true;
                break;
            }

            cancellationToken.ThrowIfCancellationRequested();

            var ancestor = await akeneoApiClient
                .GetProductModelByCodeAsync(currentCode, cancellationToken);

            if (ancestor == null)
            {
                model.Warnings.Add(
                    $"Product-model inheritance could not be fully previewed because Akeneo product model '{currentCode}' was not found.");
                break;
            }

            ancestors.Add(ancestor);
            currentCode = ancestor.Parent?.Trim();
        }

        if (cycleDetected)
        {
            model.Warnings.Add(
                $"A product-model inheritance cycle was detected at '{currentCode}'. Preview used the values resolved before the cycle.");
        }

        var familyMapping = await familyMappingService
            .GetByFamilyCodeAsync(productModel.Family);

        var hierarchyMode = familyMapping is { Enabled: true }
            ? familyMapping.ProductModelHierarchyMode
            : AkeneoProductModelHierarchyMode.ImmediateParentProductModel;

        var syntheticLeaf = new AkeneoProductDefinition
        {
            Parent = selectedCode,
            Family = productModel.Family,
            FamilyVariant = productModel.FamilyVariant,
            Values = JsonSerializer.SerializeToElement(
                new Dictionary<string, object>())
        };

        var modelChain = new List<AkeneoProductDefinition> { productModel };
        modelChain.AddRange(ancestors);

        var hierarchy = productModelHierarchyResolver.Resolve(
            syntheticLeaf,
            modelChain,
            hierarchyMode);

        var effectiveModel = hierarchy.EffectiveParentProductModel;

        model.ImmediateParentProductModelCode = productModel.Code?.Trim();
        model.EffectiveParentProductModelCode = effectiveModel.Code?.Trim();
        model.HierarchyDecision =
            $"Akeneo product model '{selectedCode}' uses {FormatHierarchyMode(hierarchy.Mode)} and resolves to nopCommerce parent product model '{model.EffectiveParentProductModelCode}'.";

        if (!string.Equals(
                selectedCode,
                effectiveModel.Code,
                StringComparison.OrdinalIgnoreCase))
        {
            model.Warnings.Add(
                $"The family hierarchy configuration resolves product model '{selectedCode}' to nopCommerce parent product model '{effectiveModel.Code}'. The preview and one-time import target the resolved parent model.");
        }

        return effectiveModel;
    }


    private async Task PopulateHierarchyDecisionAsync(
        AkeneoProductDefinition originalSource,
        AkeneoProductDefinition previewSource,
        AkeneoEntityType sourceEntityType,
        AkeneoProductImportRequest request,
        AkeneoProductSyncContext context,
        AkeneoProductMappingPreviewModel model,
        CancellationToken cancellationToken)
    {
        if (sourceEntityType == AkeneoEntityType.ProductModel)
        {
            model.ParentNopProductId = context.ExistingProduct?.Id;
            model.ParentProductDecision = context.ExistingProduct == null
                ? $"The resolved product model '{model.EffectiveParentProductModelCode ?? previewSource.Code}' would create a nopCommerce parent product. Descendant leaves are not imported by this one-time product-model action."
                : $"The resolved product model maps to existing nopCommerce parent product #{context.ExistingProduct.Id}. Descendant leaves are not imported by this one-time product-model action.";
            model.CurrentRepresentation = context.ExistingProduct == null
                ? "No existing nopCommerce parent product"
                : $"Existing nopCommerce parent product #{context.ExistingProduct.Id}";
            return;
        }

        if (string.IsNullOrWhiteSpace(originalSource.Parent))
        {
            model.CurrentRepresentation = context.ExistingProduct == null
                ? "No existing nopCommerce product"
                : $"Standalone nopCommerce product #{context.ExistingProduct.Id}";
            return;
        }

        var effectiveParentCode = model.EffectiveParentProductModelCode?.Trim();
        if (string.IsNullOrWhiteSpace(effectiveParentCode))
        {
            model.HierarchyRequiresReview = true;
            model.ParentProductDecision =
                "The effective parent product-model code could not be resolved. Parent creation and variant representation will be decided during import.";
            return;
        }

        var effectiveParentModel = await akeneoApiClient
            .GetProductModelByCodeAsync(effectiveParentCode, cancellationToken);

        if (effectiveParentModel == null)
        {
            model.HierarchyRequiresReview = true;
            model.ParentProductDecision =
                $"Akeneo product model '{effectiveParentCode}' could not be reloaded, so the parent nopCommerce product and final representation cannot be confirmed.";
            return;
        }

        var parentResult = new AkeneoProductImportResult
        {
            AkeneoIdentifier = effectiveParentCode,
            AkeneoProductKey = effectiveParentCode
        };

        var parentContext = await productSyncService.PrepareAsync(
            effectiveParentModel,
            AkeneoEntityType.ProductModel,
            request,
            parentResult,
            originalSource.Family,
            AkeneoAttributeMappingEntityScope.ProductModel,
            cancellationToken);

        model.ParentNopProductId = parentContext.ExistingProduct?.Id;
        model.ParentProductDecision = parentContext.ExistingProduct == null
            ? $"Parent product model '{effectiveParentCode}' would be created and synchronized before the leaf representation is applied."
            : $"Existing nopCommerce parent product #{parentContext.ExistingProduct.Id} for model '{effectiveParentCode}' would be synchronized before the leaf representation is applied.";

        var familyMapping = await familyMappingService
            .GetByFamilyCodeAsync(originalSource.Family);
        AkeneoProductDefinition immediateParentModel = null;
        if (string.Equals(
                model.ImmediateParentProductModelCode,
                effectiveParentCode,
                StringComparison.OrdinalIgnoreCase))
        {
            immediateParentModel = effectiveParentModel;
        }
        else if (!string.IsNullOrWhiteSpace(model.ImmediateParentProductModelCode))
        {
            immediateParentModel = await akeneoApiClient.GetProductModelByCodeAsync(
                model.ImmediateParentProductModelCode,
                cancellationToken);
        }
        var overrideRule = await ResolveSubModelOverrideAsync(
            familyMapping,
            originalSource,
            immediateParentModel);

        string representation;
        string decisionSource;

        if (overrideRule != null)
        {
            if (overrideRule.VariantRelationshipOverrideMode ==
                AkeneoVariantRelationshipMode.None)
            {
                var existingMode = await leafRepresentationClassifier.ClassifyAsync(
                    context.ExistingProduct,
                    context.Sku);

                if (existingMode is null or AkeneoVariantRelationshipMode.None)
                {
                    representation = "standalone nopCommerce product";
                    decisionSource = "matching submodel override rule";
                    model.CurrentRepresentation = existingMode == null
                        ? "No existing leaf product representation"
                        : "Existing standalone nopCommerce product";
                }
                else
                {
                    representation = FormatVariantMode(existingMode.Value);
                    decisionSource =
                        $"existing {representation} prevents the standalone submodel override from changing representation";
                    model.CurrentRepresentation = representation;
                }
            }
            else
            {
                representation = FormatVariantMode(
                    overrideRule.VariantRelationshipOverrideMode);
                decisionSource = "matching submodel override rule";
                model.CurrentRepresentation ??= context.ExistingProduct == null
                    ? "No existing leaf product representation"
                    : "Existing leaf representation will be reconciled to the forced mode";
            }
        }
        else if (parentContext.ExistingProduct != null)
        {
            try
            {
                var resolution = await variantRelationshipResolver.ResolveAsync(
                    parentContext.ExistingProduct,
                    originalSource.Family);
                representation = FormatVariantMode(resolution.Mode);
                decisionSource = resolution.Source ==
                    AkeneoVariantRelationshipSource.ExistingNopParent
                    ? "existing nopCommerce parent structure"
                    : "Akeneo family configuration";
                model.CurrentRepresentation = resolution.Source ==
                    AkeneoVariantRelationshipSource.ExistingNopParent
                    ? representation
                    : "No existing variant structure was selected";
            }
            catch (Exception ex)
            {
                representation = "unresolved variant representation";
                decisionSource = ex.Message;
                model.HierarchyRequiresReview = true;
                model.CurrentRepresentation =
                    "Existing parent structure could not be classified safely";
            }
        }
        else if (familyMapping is { Enabled: true })
        {
            representation = FormatVariantMode(familyMapping.VariantRelationshipMode);
            decisionSource = "Akeneo family configuration";
            model.CurrentRepresentation = "No existing nopCommerce parent structure";
        }
        else
        {
            representation = "unresolved variant representation";
            decisionSource =
                $"no enabled family variant configuration exists for '{originalSource.Family}'";
            model.HierarchyRequiresReview = true;
            model.CurrentRepresentation = "No existing nopCommerce parent structure";
        }

        model.HierarchyDecision =
            $"{model.HierarchyDecision} Leaf representation: {representation} ({decisionSource}).";

        if (parentResult.Errors.Any())
        {
            model.HierarchyRequiresReview = true;
            model.ParentProductDecision +=
                $" Parent preparation reported: {string.Join("; ", parentResult.Errors)}";
        }
    }

    private async Task<AkeneoFamilySubModelRule> ResolveSubModelOverrideAsync(
        AkeneoFamilyMapping familyMapping,
        AkeneoProductDefinition leaf,
        AkeneoProductDefinition subModel)
    {
        if (familyMapping is not { Enabled: true } || subModel == null)
            return null;

        var rules = await familyMappingService.GetSubModelRulesAsync(
            familyMapping.Id);

        return AkeneoSubModelRuleMatcher.FindMatch(rules, leaf, subModel);
    }

    private static string FormatHierarchyMode(
        AkeneoProductModelHierarchyMode mode) => mode switch
    {
        AkeneoProductModelHierarchyMode.RootProductModel =>
            "root-product-model flattening",
        _ => "immediate-parent-product-model hierarchy"
    };

    private static string FormatVariantMode(
        AkeneoVariantRelationshipMode mode,
        bool standaloneForNone = false) => mode switch
    {
        AkeneoVariantRelationshipMode.GroupedProducts => "grouped child product",
        AkeneoVariantRelationshipMode.AssociatedToProductAttributeValue =>
            "associated product attribute value",
        AkeneoVariantRelationshipMode.ProductAttributeCombinations =>
            "product attribute combination",
        AkeneoVariantRelationshipMode.None when standaloneForNone =>
            "standalone nopCommerce product",
        _ => "no configured variant relationship"
    };

    private async Task<AkeneoProductDefinition> FindAkeneoProductByIdentifierAsync(
        string akeneoIdentifier,
        CancellationToken cancellationToken)
    {
        var searchJson = JsonSerializer.Serialize(new
        {
            identifier = new[]
            {
                new
                {
                    @operator = "=",
                    value = akeneoIdentifier
                }
            }
        });

        var products = await akeneoApiClient.GetProductsAsync(
            searchJson,
            limit: 1,
            cancellationToken: cancellationToken);

        return products.FirstOrDefault();
    }

    private static void AddMappingCoverageWarnings(
        AkeneoProductMappingPreviewModel model,
        AkeneoProductSyncContext context)
    {
        var productFields = context.GetMappings(NopTargetType.ProductField).ToList();

        if (!model.IsProductModel && !productFields.Any(field => string.Equals(
                field.Mapping.NopTargetKey,
                "Sku",
                StringComparison.OrdinalIgnoreCase)))
        {
            model.Warnings.Add("No active mapping targets Product.Sku.");
        }

        if (!productFields.Any(field => string.Equals(
                field.Mapping.NopTargetKey,
                "Name",
                StringComparison.OrdinalIgnoreCase)))
        {
            model.Warnings.Add("No active mapping targets Product.Name.");
        }

        //if (!productFields.Any(field => string.Equals(
        //        field.Mapping.NopTargetKey,
        //        "Price",
        //        StringComparison.OrdinalIgnoreCase)))
        //{
        //    model.Warnings.Add("No active mapping targets Product.Price.");
        //}
    }

    private static void AppendDistinct(
        ICollection<string> destination,
        IEnumerable<string> values)
    {
        foreach (var value in values.Where(value =>
                     !string.IsNullOrWhiteSpace(value)))
        {
            if (!destination.Contains(value, StringComparer.OrdinalIgnoreCase))
                destination.Add(value);
        }
    }
}
