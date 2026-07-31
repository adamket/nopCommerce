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
            return product;

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
            return product;

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

        if (!productFields.Any(field => string.Equals(
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
