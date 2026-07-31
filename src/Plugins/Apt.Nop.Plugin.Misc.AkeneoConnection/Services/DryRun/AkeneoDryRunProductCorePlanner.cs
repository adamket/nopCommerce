using System.Globalization;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Nop.Core.Domain.Catalog;
using static Apt.Nop.Plugin.Misc.AkeneoConnection.Services.DryRun.AkeneoDryRunPlanHelper;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services.DryRun;

public sealed class AkeneoDryRunProductCorePlanner : IAkeneoDryRunSectionPlanner
{
    public int Order => 100;

    public Task PlanAsync(
        AkeneoProductSyncContext context,
        AkeneoProductMappingPreviewModel model,
        CancellationToken cancellationToken = default)
    {
        var product = context.ExistingProduct;
        var isNew = product == null;

        model.NopProductFound = !isNew;
        model.NopProductId = product?.Id;

        ApplyWritePolicy(context, model, isNew);
        AddLifecycleOperation(context, model, isNew);
        AnalyzeProductFields(context, model, product, cancellationToken);

        return Task.CompletedTask;
    }

    private static void ApplyWritePolicy(
        AkeneoProductSyncContext context,
        AkeneoProductMappingPreviewModel model,
        bool isNew)
    {
        if (isNew && !context.Request.CreateNewProducts)
        {
            model.ImportAllowedByProfile = false;
            model.ImportBlockReason =
                "The saved sync profile is configured for updates only, so this missing nopCommerce product would be skipped.";
            return;
        }

        if (!isNew && !context.Request.UpdateExistingProducts)
        {
            model.ImportAllowedByProfile = false;
            model.ImportBlockReason =
                "The saved sync profile is configured for creation only, so this existing nopCommerce product would be skipped.";
        }
    }

    private static void AddLifecycleOperation(
        AkeneoProductSyncContext context,
        AkeneoProductMappingPreviewModel model,
        bool isNew)
    {
        if (isNew)
        {
            AddOperation(
                model,
                "Product lifecycle",
                "nopCommerce product",
                context.SourceCode,
                null,
                "Create product",
                AkeneoDryRunOperationType.Create,
                "A new simple, individually visible nopCommerce product would be created before mapped sections are synchronized.");
            return;
        }

        var product = context.ExistingProduct;
        AddOperation(
            model,
            "Product lifecycle",
            "nopCommerce product",
            context.SourceCode,
            $"Product #{product.Id}",
            $"Product #{product.Id}",
            AkeneoDryRunOperationType.NoChange,
            "The existing product is the destination resolved by the real synchronization prepare path.");

        var syncState = context.ExistingSyncState;
        if (syncState == null)
            return;

        var lifecycleStatus =
            (AkeneoProductLifecycleStatus)syncState.LifecycleStatusId;

        if (lifecycleStatus == AkeneoProductLifecycleStatus.PurchasingDisabled)
        {
            AddOperation(
                model,
                "Product lifecycle",
                "DisableBuyButton",
                "Previous Akeneo lifecycle state",
                product.DisableBuyButton.ToString(),
                bool.FalseString,
                DetermineScalarChangeType(
                    product.DisableBuyButton.ToString(),
                    bool.FalseString),
                "The synchronization pipeline restores purchasing before applying mapped values.");
        }

        if (lifecycleStatus == AkeneoProductLifecycleStatus.SoftDeleted)
        {
            AddOperation(
                model,
                "Product lifecycle",
                "Deleted",
                "Previous Akeneo lifecycle state",
                product.Deleted.ToString(),
                bool.FalseString,
                DetermineScalarChangeType(
                    product.Deleted.ToString(),
                    bool.FalseString),
                "The synchronization pipeline restores a soft-deleted product before applying mapped values.");
        }

        var hasPublishedMapping = context
            .GetMappings(NopTargetType.ProductField)
            .Any(mapped => string.Equals(
                mapped.Mapping.NopTargetKey,
                "Published",
                StringComparison.OrdinalIgnoreCase));

        if ((lifecycleStatus is AkeneoProductLifecycleStatus.Unpublished or
                AkeneoProductLifecycleStatus.SoftDeleted) &&
            !hasPublishedMapping)
        {
            var proposedPublished = (context.Source.Enabled ?? true).ToString();
            AddOperation(
                model,
                "Product lifecycle",
                "Published",
                "Akeneo lifecycle restoration",
                product.Published.ToString(),
                proposedPublished,
                DetermineScalarChangeType(
                    product.Published.ToString(),
                    proposedPublished),
                "No Published mapping exists, so the source enabled state restores publication.");
        }

        var restoresFormerCombination =
            (AkeneoProductDestinationKind)syncState.DestinationKindId ==
                AkeneoProductDestinationKind.ProductAttributeCombination &&
            product.Id != syncState.NopProductId;

        if (!restoresFormerCombination)
            return;

        if (lifecycleStatus != AkeneoProductLifecycleStatus.PurchasingDisabled)
        {
            AddOperation(
                model,
                "Product lifecycle",
                "DisableBuyButton",
                "Previous product-attribute-combination representation",
                product.DisableBuyButton.ToString(),
                bool.FalseString,
                DetermineScalarChangeType(
                    product.DisableBuyButton.ToString(),
                    bool.FalseString),
                "A previously hidden child product would be re-enabled before its new representation is applied.");
        }

        if (lifecycleStatus is not (AkeneoProductLifecycleStatus.Unpublished or
                AkeneoProductLifecycleStatus.SoftDeleted) &&
            !hasPublishedMapping &&
            context.Source.Enabled.HasValue)
        {
            var proposedPublished = context.Source.Enabled.Value.ToString();
            AddOperation(
                model,
                "Product lifecycle",
                "Published",
                "Previous product-attribute-combination representation",
                product.Published.ToString(),
                proposedPublished,
                DetermineScalarChangeType(
                    product.Published.ToString(),
                    proposedPublished),
                "The source enabled state is restored because no Published mapping overrides it.");
        }
    }

    private static void AnalyzeProductFields(
        AkeneoProductSyncContext context,
        AkeneoProductMappingPreviewModel model,
        Product product,
        CancellationToken cancellationToken)
    {
        var mappings = context.GetMappings(NopTargetType.ProductField).ToList();
        var mappedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var mapped in mappings)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var key = mapped.Mapping.NopTargetKey?.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                AddReviewOperation(
                    model,
                    "Product fields",
                    "Missing target key",
                    GetSourceName(mapped),
                    "The mapping cannot be applied because no nopCommerce field target is configured.",
                    mapped.Mapping.IsRequired);
                continue;
            }

            mappedKeys.Add(key);

            var current = GetProductFieldValue(product, key);
            if (!TryResolveProductFieldValue(
                    context,
                    mapped,
                    key,
                    current,
                    out var proposed,
                    out var type,
                    out var detail))
            {
                AddReviewOperation(
                    model,
                    "Product fields",
                    key,
                    GetSourceName(mapped),
                    detail,
                    mapped.Mapping.IsRequired,
                    current,
                    mapped.DisplayValue);
                continue;
            }

            if (product == null && type != AkeneoDryRunOperationType.Review)
                type = AkeneoDryRunOperationType.Create;

            AddOperation(
                model,
                "Product fields",
                key,
                GetSourceName(mapped),
                current,
                proposed,
                type,
                detail,
                mapped.Mapping.IsRequired);
        }

        if (product != null)
            return;

        var fallbackName = context.Sku ?? context.SourceCode ?? context.ProductKey;

        AddCreationDefaultIfUnmapped(
            model,
            mappedKeys,
            "Sku",
            fallbackName,
            "nopCommerce creation default");

        AddCreationDefaultIfUnmapped(
            model,
            mappedKeys,
            "Name",
            fallbackName,
            "nopCommerce creation default");

        AddCreationDefaultIfUnmapped(
            model,
            mappedKeys,
            "Published",
            (context.Source.Enabled ?? false).ToString(),
            "Akeneo enabled state");
    }

    private static void AddCreationDefaultIfUnmapped(
        AkeneoProductMappingPreviewModel model,
        ISet<string> mappedKeys,
        string target,
        string value,
        string source)
    {
        if (mappedKeys.Contains(target))
            return;

        AddOperation(
            model,
            "Product fields",
            target,
            source,
            null,
            value,
            AkeneoDryRunOperationType.Create,
            "No active mapping targets this required creation field, so the synchronization pipeline uses its creation default.");
    }

    private static string GetProductFieldValue(Product product, string targetKey)
    {
        if (product == null)
            return null;

        return targetKey switch
        {
            "Name" => product.Name,
            "ShortDescription" => product.ShortDescription,
            "FullDescription" => product.FullDescription,
            "Sku" => product.Sku,
            "Price" => product.Price.ToString(CultureInfo.InvariantCulture),
            "StockQuantity" => product.StockQuantity.ToString(CultureInfo.InvariantCulture),
            "Gtin" => product.Gtin,
            "ManufacturerPartNumber" => product.ManufacturerPartNumber,
            "Published" => product.Published.ToString(),
            _ => null
        };
    }

    private static bool TryResolveProductFieldValue(
        AkeneoProductSyncContext context,
        AkeneoResolvedMappedValue mapped,
        string targetKey,
        string current,
        out string proposed,
        out AkeneoDryRunOperationType type,
        out string detail)
    {
        proposed = current;
        type = AkeneoDryRunOperationType.NoChange;
        detail = null;

        if (!mapped.HasValue &&
            context.Request.ProductFieldMissingValueBehavior ==
                AkeneoMissingValueBehavior.PreserveExisting)
        {
            type = AkeneoDryRunOperationType.Preserve;
            detail = "The mapping produced no value and the profile preserves existing product-field values.";
            return true;
        }

        var value = mapped.HasValue ? mapped.DisplayValue : string.Empty;

        switch (targetKey)
        {
            case "Name":
                proposed = mapped.HasValue ? value : context.ProductKey;
                break;

            case "ShortDescription":
            case "FullDescription":
            case "Gtin":
            case "ManufacturerPartNumber":
                proposed = value;
                break;

            case "Sku":
                if (!mapped.HasValue)
                {
                    type = AkeneoDryRunOperationType.Preserve;
                    detail = "SKU is an identity field and is preserved when the mapped Akeneo value is empty.";
                    return true;
                }

                proposed = value;
                break;

            case "Price":
                if (!mapped.HasValue)
                {
                    proposed = "0";
                }
                else if (decimal.TryParse(
                             value,
                             NumberStyles.Number,
                             CultureInfo.InvariantCulture,
                             out var price))
                {
                    proposed = price.ToString(CultureInfo.InvariantCulture);
                }
                else
                {
                    detail = $"The value '{value}' cannot be parsed as a nopCommerce price. The real import will warn and leave the current price unchanged.";
                    return false;
                }

                break;

            case "StockQuantity":
                if (!mapped.HasValue)
                {
                    proposed = "0";
                }
                else if (int.TryParse(
                             value,
                             NumberStyles.Integer,
                             CultureInfo.InvariantCulture,
                             out var quantity))
                {
                    proposed = quantity.ToString(CultureInfo.InvariantCulture);
                }
                else
                {
                    detail = $"The value '{value}' cannot be parsed as an integer stock quantity. The real import will warn and preserve the current quantity.";
                    return false;
                }

                break;

            case "Published":
                if (!mapped.HasValue)
                {
                    proposed = bool.FalseString;
                }
                else if (TryParseBoolean(value, out var published))
                {
                    proposed = published.ToString();
                }
                else
                {
                    detail = $"The value '{value}' cannot be parsed as a Boolean. The real import will warn and preserve the current published state.";
                    return false;
                }

                break;

            default:
                detail = $"'{targetKey}' is not supported by the current product-field synchronizer.";
                return false;
        }

        type = DetermineScalarChangeType(current, proposed);
        return true;
    }
}
