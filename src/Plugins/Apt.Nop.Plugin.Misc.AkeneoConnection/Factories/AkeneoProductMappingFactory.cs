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
    IAkeneoDryRunChangeAnalyzer changeAnalyzer,
    IAkeneoProfileRuleEvaluator profileRuleEvaluator,
    IAkeneoCombinationAvailabilityService combinationAvailabilityService)
    : IAkeneoProductMappingFactory
{
    public async Task<AkeneoProductMappingPreviewModel> PreviewProductMappingAsync(
        string akeneoIdentifier,
        AkeneoSyncProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var valueContext = AkeneoValueContext.FromProfile(profile);

        var model = new AkeneoProductMappingPreviewModel
        {
            AkeneoIdentifier = akeneoIdentifier,
            Locale = valueContext.Locale,
            Channel = valueContext.Channel,
            Currency = valueContext.Currency,
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

        var request = requestFactory.CreateFromProfile(
            profile,
            syncRunRecordId: 0,
            runMode: AkeneoRunMode.SingleProduct);

        // The request already carries the profile's value context.
        request.AkeneoProductUuid = model.AkeneoProductUuid;
        request.AkeneoProductModelCode = model.AkeneoProductModelCode;

        try
        {
            await combinationAvailabilityService.ValidateRulesAsync(request.AkeneoFamilyCodes,
                request.ProductEnabledFilter, request.UpdateExistingProducts);
        }
        catch (ArgumentException ex)
        {
            model.Errors.Add(ex.Message);
            model.ImportAllowedByProfile = false;
            model.Action = "Family combination availability conflicts with the profile settings.";
            return model;
        }

        // Check the requested source before hierarchy selection can replace a
        // held submodel with its root model in the preview.
        try
        {
            var skipReason = await profileRuleEvaluator.GetSkipReasonAsync(akeneoSource, request, cancellationToken);
            if (skipReason != null)
            {
                model.Messages.Add(skipReason);
                await changeAnalyzer.AnalyzeAsync(new AkeneoProductSyncContext { SkipReason = skipReason }, model, cancellationToken);
                return model;
            }
        }
        catch (InvalidOperationException ex)
        {
            model.Errors.Add(ex.Message);
            model.ImportAllowedByProfile = false;
            model.ImportBlockReason = "Profile rule evaluation failed; no product changes will be made.";
            model.Action = model.ImportBlockReason;
            return model;
        }

        if (sourceEntityType == AkeneoEntityType.Product)
        {
            try
            {
                var unavailable = await combinationAvailabilityService.GetUnavailableBindingAsync(akeneoSource, request, cancellationToken);
                if (unavailable != null)
                {
                    var removes = unavailable.Parent is { Deleted: false } && unavailable.CombinationExists;
                    model.NopProductId = unavailable.State.NopProductId;
                    model.ParentNopProductId = unavailable.State.NopProductId;
                    model.NopProductFound = unavailable.Parent != null;
                    model.Action = removes ? "Remove unavailable attribute combination" : "Preserve parent; combination unavailable";
                    model.ParentProductDecision = "The parent product and attribute values shared by remaining combinations are preserved.";
                    model.Operations.Add(new AkeneoDryRunOperationPreviewModel
                    {
                        Area = "Variant relationship", Target = "Attribute combination",
                        ChangeType = removes ? AkeneoDryRunOperationType.Remove : AkeneoDryRunOperationType.Skip,
                        Detail = $"Akeneo SKU is disabled. {model.ParentProductDecision}"
                    });
                    return model;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                model.Errors.Add(ex.Message);
                model.ImportAllowedByProfile = false;
                model.Action = "Akeneo enabled status could not be evaluated; no changes will be made.";
                return model;
            }
        }

        AkeneoProductDefinition previewSource;
        string mappingFamilyVariantCode;
        AkeneoAttributeMappingEntityScope? modelMappingScope = null;

        if (sourceEntityType == AkeneoEntityType.ProductModel)
        {
            (previewSource, var scope) = await BuildEffectiveProductModelPreviewSourceAsync(
                akeneoSource,
                model,
                cancellationToken);
            modelMappingScope = scope;
            mappingFamilyVariantCode = previewSource.FamilyVariant?.Trim()
                ?? akeneoSource.FamilyVariant?.Trim();
        }
        else
        {
            var previewResolution = await BuildEffectivePreviewSourceAsync(
                akeneoSource,
                model,
                cancellationToken);
            previewSource = previewResolution.Source;
            mappingFamilyVariantCode = previewResolution.FamilyVariantCode;

        }

        if (sourceEntityType == AkeneoEntityType.ProductModel)
            model.AkeneoProductModelCode = previewSource.Code;

        request.AkeneoProductModelCode = model.AkeneoProductModelCode;

        var result = new AkeneoProductImportResult
        {
            AkeneoProductUuid = model.AkeneoProductUuid,
            AkeneoIdentifier = sourceEntityType == AkeneoEntityType.ProductModel
                ? model.AkeneoProductModelCode
                : previewSource.Identifier
        };

        var mappingFamilyCode = previewSource.Family ?? akeneoSource.Family;
        var mappingEntityScope = modelMappingScope ??
            AkeneoAttributeMappingScopeHelper.ResolveDefaultCurrentScope(
                previewSource,
                sourceEntityType);

        var context = await productSyncService.PrepareAsync(
            previewSource,
            sourceEntityType,
            request,
            result,
            mappingFamilyCode,
            mappingFamilyVariantCode,
            mappingEntityScope,
            cancellationToken);

        if (!result.Success)
        {
            AppendDistinct(model.Errors, result.Errors);
            model.ImportAllowedByProfile = false;
            model.ImportBlockReason = "Profile rule evaluation failed; no product changes will be made.";
            model.Action = model.ImportBlockReason;
            return model;
        }
        if (context.SkipReason != null)
        {
            AppendDistinct(model.Messages, result.Messages);
            await changeAnalyzer.AnalyzeAsync(context, model, cancellationToken);
            return model;
        }

        var previewMode = await PopulateHierarchyDecisionAsync(
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

        if (previewMode == AkeneoVariantRelationshipMode.ProductAttributeCombinations)
        {
            try
            {
                var availabilitySource = await combinationAvailabilityService.GetAvailabilitySourceAsync(akeneoSource, cancellationToken);
                var availabilityContext = new AkeneoVariantImportContext
                {
                    SourceProduct = previewSource, AkeneoIdentifier = akeneoSource.Identifier,
                    AkeneoUuid = akeneoSource.Uuid, Sku = context.Sku,
                    AkeneoFamilyCode = akeneoSource.Family, AkeneoFamilyVariantCode = context.MappingFamilyVariantCode,
                    Locale = request.Locale, Channel = request.Channel, Currency = request.Currency,
                    ManageVariantsByEnabledStatus = availabilitySource != null,
                    CombinationAvailabilitySource = availabilitySource ?? AkeneoCombinationAvailabilitySource.EnabledStatus
                };
                if (availabilitySource == AkeneoCombinationAvailabilitySource.PublishedRule)
                    AkeneoCombinationAvailabilityService.ApplyPublishedRule(availabilityContext, context.MappedValues);

                if (!await combinationAvailabilityService.IsAvailableAsync(availabilityContext, cancellationToken))
                {
                    model.Action = "Do not create unavailable attribute combination";
                    model.Operations.Add(new AkeneoDryRunOperationPreviewModel
                    {
                        Area = "Variant relationship", Target = "Attribute combination",
                        ChangeType = AkeneoDryRunOperationType.Skip,
                        Detail = availabilitySource == AkeneoCombinationAvailabilitySource.PublishedRule
                            ? (availabilityContext.PublishedRuleHeld
                                ? "The Published rule is held and no combination exists yet, so none will be created. An existing one would be kept."
                                : "The Published rule is false. An existing combination would be removed and none will be created. Parent synchronization still follows the profile.")
                            : "Akeneo SKU is disabled. No combination or axis values will be created. Parent synchronization still follows the profile."
                    });
                    return model;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                model.Errors.Add(ex.Message);
                model.ImportAllowedByProfile = false;
                model.Action = "Combination availability could not be evaluated; no changes will be made.";
                return model;
            }
        }

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

    private async Task<(AkeneoProductDefinition Source, string FamilyVariantCode)>
        BuildEffectivePreviewSourceAsync(
        AkeneoProductDefinition product,
        AkeneoProductMappingPreviewModel model,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(product.Parent))
        {
            model.HierarchyDecision =
                $"Akeneo product '{product.Identifier ?? product.Uuid}' is standalone and will synchronize directly to one nopCommerce product.";
            model.HierarchyRequiresReview = false;
            return (product, null);
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
            return (product, null);
        }

        if (cycleDetected)
        {
            model.Warnings.Add(
                $"A product-model inheritance cycle was detected at '{currentCode}'. Preview used the values resolved before the cycle.");
        }

        var familyVariantResolution = AkeneoLeafFamilyVariantResolver.Resolve(
            product,
            ancestors);

        if (!familyVariantResolution.Success)
        {
            model.ImmediateParentProductModelCode =
                familyVariantResolution.ImmediateParentCode;
            model.EffectiveParentProductModelCode =
                familyVariantResolution.ImmediateParentCode;
            model.HierarchyDecision =
                $"Akeneo leaf '{product.Identifier ?? product.Uuid}' references parent model '{familyVariantResolution.ImmediateParentCode}', but the parent does not expose a family-variant code.";
            model.HierarchyRequiresReview = true;
            model.Errors.Add(familyVariantResolution.Error);
            return (product, null);
        }

        var familyVariantCode = familyVariantResolution.FamilyVariantCode;

        var familyMapping = await familyMappingService
            .GetEffectiveMappingAsync(
                product.Family,
                familyVariantCode);

        var hierarchyMode = familyMapping is { Enabled: true }
            ? familyMapping.ProductModelHierarchyMode
            : AkeneoProductModelHierarchyMode.ImmediateParentProductModel;

        var hierarchy = productModelHierarchyResolver.Resolve(
            product,
            ancestors,
            hierarchyMode);

        var subModelRule = await ResolveSubModelOverrideAsync(
            familyMapping,
            product,
            hierarchy.ImmediateParentModel);

        if (subModelRule.UsesSubModelParent())
            hierarchy = productModelHierarchyResolver.ResolveSubModelParent(product, ancestors);

        model.ImmediateParentProductModelCode =
            hierarchy.ImmediateParentModel?.Code ?? product.Parent?.Trim();
        model.EffectiveParentProductModelCode =
            hierarchy.EffectiveParentProductModel?.Code ?? product.Parent?.Trim();
        model.HierarchyDecision = hierarchy.IsSubModelParent
            ? $"Akeneo leaf '{product.Identifier ?? product.Uuid}' matches a sub-model parent rule: " +
              $"sub-model '{model.EffectiveParentProductModelCode}' is the nopCommerce parent."
            : $"Akeneo leaf '{product.Identifier ?? product.Uuid}' uses {FormatHierarchyMode(hierarchy.Mode)}: " +
              $"immediate parent '{model.ImmediateParentProductModelCode}', effective nopCommerce parent '{model.EffectiveParentProductModelCode}'.";

        return (hierarchy.EffectiveLeaf, familyVariantCode);
    }

    private async Task<(AkeneoProductDefinition Model, AkeneoAttributeMappingEntityScope Scope)>
        BuildEffectiveProductModelPreviewSourceAsync(
            AkeneoProductDefinition productModel,
            AkeneoProductMappingPreviewModel model,
            CancellationToken cancellationToken)
    {
        var selectedCode = productModel.Code?.Trim();
        AkeneoAttributeMappingEntityScope parentMappingScope;
        if (string.IsNullOrWhiteSpace(selectedCode))
            return (productModel, AkeneoAttributeMappingEntityScope.ProductModel);

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
            .GetEffectiveMappingAsync(
                productModel.Family,
                productModel.FamilyVariant);

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

        // Must match AkeneoProductBatchSyncService.ResolveEffectiveProductModelAsync.
        var subModelRule = await ResolveSubModelOverrideAsync(familyMapping, syntheticLeaf, productModel);
        var hierarchy = subModelRule.UsesSubModelParent()
            ? productModelHierarchyResolver.ResolveSubModelParent(syntheticLeaf, modelChain)
            : productModelHierarchyResolver.Resolve(syntheticLeaf, modelChain, hierarchyMode);

        var effectiveModel = hierarchy.EffectiveParentProductModel;
        parentMappingScope = hierarchy.ParentMappingScope;

        model.ImmediateParentProductModelCode = productModel.Code?.Trim();
        model.EffectiveParentProductModelCode = effectiveModel.Code?.Trim();
        model.HierarchyDecision = hierarchy.IsSubModelParent
            ? $"Akeneo product model '{selectedCode}' matches a sub-model parent rule and is its own nopCommerce parent, built with Sub-model parent mappings."
            : $"Akeneo product model '{selectedCode}' uses {FormatHierarchyMode(hierarchy.Mode)} and resolves to nopCommerce parent product model '{model.EffectiveParentProductModelCode}'.";

        if (!string.Equals(
                selectedCode,
                effectiveModel.Code,
                StringComparison.OrdinalIgnoreCase))
        {
            model.Warnings.Add(
                $"The family hierarchy configuration resolves product model '{selectedCode}' to nopCommerce parent product model '{effectiveModel.Code}'. The preview and one-time import target the resolved parent model.");
        }

        return (effectiveModel, parentMappingScope);
    }


    private async Task<AkeneoVariantRelationshipMode?> PopulateHierarchyDecisionAsync(
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
            return null;
        }

        if (string.IsNullOrWhiteSpace(originalSource.Parent))
        {
            model.CurrentRepresentation = context.ExistingProduct == null
                ? "No existing nopCommerce product"
                : $"Standalone nopCommerce product #{context.ExistingProduct.Id}";
            return null;
        }

        var effectiveParentCode = model.EffectiveParentProductModelCode?.Trim();
        if (string.IsNullOrWhiteSpace(effectiveParentCode))
        {
            model.HierarchyRequiresReview = true;
            model.ParentProductDecision =
                "The effective parent product-model code could not be resolved. Parent creation and variant representation will be decided during import.";
            return null;
        }

        var effectiveParentModel = await akeneoApiClient
            .GetProductModelByCodeAsync(effectiveParentCode, cancellationToken);

        if (effectiveParentModel == null)
        {
            model.HierarchyRequiresReview = true;
            model.ParentProductDecision =
                $"Akeneo product model '{effectiveParentCode}' could not be reloaded, so the parent nopCommerce product and final representation cannot be confirmed.";
            return null;
        }

        var parentResult = new AkeneoProductImportResult
        {
            AkeneoIdentifier = effectiveParentCode,
            AkeneoProductKey = effectiveParentCode
        };

        var familyMapping = await familyMappingService
            .GetEffectiveMappingAsync(
                originalSource.Family,
                context.MappingFamilyVariantCode);
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

        var parentContext = await productSyncService.PrepareAsync(
            effectiveParentModel,
            AkeneoEntityType.ProductModel,
            request,
            parentResult,
            originalSource.Family,
            context.MappingFamilyVariantCode,
            overrideRule.UsesSubModelParent()
                ? AkeneoAttributeMappingEntityScope.SubModelParent
                : AkeneoAttributeMappingEntityScope.ProductModel,
            cancellationToken);

        model.ParentNopProductId = parentContext.ExistingProduct?.Id;
        model.ParentProductDecision = parentContext.ExistingProduct == null
            ? $"Parent product model '{effectiveParentCode}' would be created and synchronized before the leaf representation is applied."
            : $"Existing nopCommerce parent product #{parentContext.ExistingProduct.Id} for model '{effectiveParentCode}' would be synchronized before the leaf representation is applied.";

        AkeneoVariantRelationshipMode? selectedMode = null;
        string representation;
        string decisionSource;

        if (overrideRule.UsesSubModelParent())
        {
            selectedMode = AkeneoVariantRelationshipMode.AssociatedToProductAttributeValue;
            representation = FormatVariantMode(selectedMode.Value);
            decisionSource = "matching sub-model parent rule";
            model.CurrentRepresentation = context.ExistingProduct == null
                ? "No existing leaf product representation"
                : $"Existing nopCommerce product #{context.ExistingProduct.Id} becomes an associated child of the sub-model parent";
        }
        else if (overrideRule != null)
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
                    selectedMode = existingMode;
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
                    originalSource.Family,
                    context.MappingFamilyVariantCode);
                selectedMode = resolution.Mode;
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
            selectedMode = familyMapping.VariantRelationshipMode;
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
        return selectedMode;
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
        var productFields = context.GetMappings(NopTargetType.ProductField, includeSkipped: true).ToList();

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
