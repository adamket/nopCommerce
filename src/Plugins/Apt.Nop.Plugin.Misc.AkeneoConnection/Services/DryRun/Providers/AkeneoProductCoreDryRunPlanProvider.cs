using System.Globalization;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services.Planning;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Nop.Core.Domain.Catalog;
using static Apt.Nop.Plugin.Misc.AkeneoConnection.Services.DryRun.AkeneoDryRunPlanHelper;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public partial class AkeneoProductCoreSynchronizer
{
    public async Task PlanAsync(
        AkeneoProductSyncContext context,
        AkeneoProductMappingPreviewModel model,
        CancellationToken cancellationToken = default)
    {
        var product = context.ExistingProduct ?? CreateBaseProduct(context);
        var isNew = context.ExistingProduct == null;
        var plan = await BuildPlanAsync(context, product, isNew, cancellationToken);

        model.NopProductFound = !isNew;
        model.NopProductId = context.ExistingProduct?.Id;
        model.ImportAllowedByProfile = plan.ImportAllowedByProfile;
        model.ImportBlockReason = plan.ImportBlockReason;

        plan.Render(model);
    }

    private static Task<AkeneoExecutableSectionPlan> BuildPlanAsync(
        AkeneoProductSyncContext context,
        Product product,
        bool isNew,
        CancellationToken cancellationToken)
    {
        var plan = new AkeneoExecutableSectionPlan();

        ApplyWritePolicy(context, plan, isNew);
        AddLifecycleOperations(context, plan, product, isNew);
        context.PlanningState.ProposedProductName = AnalyzeProductFields(
            context,
            plan,
            product,
            isNew,
            cancellationToken);

        return Task.FromResult(plan);
    }

    private static void ApplyWritePolicy(
        AkeneoProductSyncContext context,
        AkeneoExecutableSectionPlan plan,
        bool isNew)
    {
        if (isNew && !context.Request.CreateNewProducts)
        {
            plan.ImportAllowedByProfile = false;
            plan.ImportBlockReason =
                "The saved sync profile is configured for updates only, so this missing nopCommerce product would be skipped.";
            return;
        }

        if (!isNew && !context.Request.UpdateExistingProducts)
        {
            plan.ImportAllowedByProfile = false;
            plan.ImportBlockReason =
                "The saved sync profile is configured for creation only, so this existing nopCommerce product would be skipped.";
        }
    }

    private static void AddLifecycleOperations(
        AkeneoProductSyncContext context,
        AkeneoExecutableSectionPlan plan,
        Product product,
        bool isNew)
    {
        if (isNew)
        {
            plan.AddOperation(
                "Product lifecycle",
                "nopCommerce product",
                context.SourceCode,
                null,
                "Create product",
                AkeneoDryRunOperationType.Create,
                "A new simple, individually visible nopCommerce product would be created before mapped sections are synchronized.");
            return;
        }

        plan.AddOperation(
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
            AddBooleanAssignment(
                plan,
                product,
                "Product lifecycle",
                "DisableBuyButton",
                "Previous Akeneo lifecycle state",
                product.DisableBuyButton,
                false,
                value => product.DisableBuyButton = value,
                "The synchronization pipeline restores purchasing before applying mapped values.");
        }

        if (lifecycleStatus == AkeneoProductLifecycleStatus.SoftDeleted)
        {
            AddBooleanAssignment(
                plan,
                product,
                "Product lifecycle",
                "Deleted",
                "Previous Akeneo lifecycle state",
                product.Deleted,
                false,
                value => product.Deleted = value,
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
            var proposedPublished = context.Source.Enabled ?? true;
            AddBooleanAssignment(
                plan,
                product,
                "Product lifecycle",
                "Published",
                "Akeneo lifecycle restoration",
                product.Published,
                proposedPublished,
                value => product.Published = value,
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
            AddBooleanAssignment(
                plan,
                product,
                "Product lifecycle",
                "DisableBuyButton",
                "Previous product-attribute-combination representation",
                product.DisableBuyButton,
                false,
                value => product.DisableBuyButton = value,
                "A previously hidden child product would be re-enabled before its new representation is applied.");
        }

        if (lifecycleStatus is not (AkeneoProductLifecycleStatus.Unpublished or
                AkeneoProductLifecycleStatus.SoftDeleted) &&
            !hasPublishedMapping &&
            context.Source.Enabled.HasValue)
        {
            AddBooleanAssignment(
                plan,
                product,
                "Product lifecycle",
                "Published",
                "Previous product-attribute-combination representation",
                product.Published,
                context.Source.Enabled.Value,
                value => product.Published = value,
                "The source enabled state is restored because no Published mapping overrides it.");
        }
    }

    private static string AnalyzeProductFields(
        AkeneoProductSyncContext context,
        AkeneoExecutableSectionPlan plan,
        Product product,
        bool isNew,
        CancellationToken cancellationToken)
    {
        var mappings = context.GetMappings(NopTargetType.ProductField).ToList();
        var mappedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var proposedProductName = product.Name ??
            context.Sku ??
            context.SourceCode ??
            context.ProductKey;

        foreach (var mapped in mappings)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var key = mapped.Mapping.NopTargetKey?.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                plan.AddReview(
                    "Product fields",
                    "Missing target key",
                    GetSourceName(mapped),
                    "The mapping cannot be applied because no nopCommerce field target is configured.",
                    mapped.Mapping.IsRequired);
                continue;
            }

            mappedKeys.Add(key);

            var current = GetProductFieldValue(isNew ? null : product, key);
            if (!TryResolveProductFieldValue(
                    context,
                    mapped,
                    key,
                    current,
                    out var proposed,
                    out var type,
                    out var detail))
            {
                plan.AddReview(
                    "Product fields",
                    key,
                    GetSourceName(mapped),
                    detail,
                    mapped.Mapping.IsRequired,
                    current,
                    mapped.DisplayValue);
                continue;
            }

            if (string.Equals(key, "Name", StringComparison.OrdinalIgnoreCase))
                proposedProductName = proposed;

            if (isNew && type != AkeneoDryRunOperationType.Review)
                type = AkeneoDryRunOperationType.Create;

            plan.AddOperation(
                "Product fields",
                key,
                GetSourceName(mapped),
                current,
                proposed,
                type,
                detail,
                mapped.Mapping.IsRequired,
                executeAsync: type is AkeneoDryRunOperationType.Create or
                    AkeneoDryRunOperationType.Update or
                    AkeneoDryRunOperationType.Clear
                    ? _ => Task.FromResult(ApplyProductField(product, key, proposed))
                    : null);
        }

        if (!isNew)
            return proposedProductName;

        var fallbackName = context.Sku ?? context.SourceCode ?? context.ProductKey;

        AddCreationDefaultIfUnmapped(
            plan,
            mappedKeys,
            product,
            "Sku",
            fallbackName,
            "nopCommerce creation default");

        AddCreationDefaultIfUnmapped(
            plan,
            mappedKeys,
            product,
            "Name",
            fallbackName,
            "nopCommerce creation default");

        AddCreationDefaultIfUnmapped(
            plan,
            mappedKeys,
            product,
            "Published",
            (context.Source.Enabled ?? false).ToString(),
            "Akeneo enabled state");

        return proposedProductName;
    }

    private static void AddCreationDefaultIfUnmapped(
        AkeneoExecutableSectionPlan plan,
        ISet<string> mappedKeys,
        Product product,
        string target,
        string value,
        string source)
    {
        if (mappedKeys.Contains(target))
            return;

        plan.AddOperation(
            "Product fields",
            target,
            source,
            null,
            value,
            AkeneoDryRunOperationType.Create,
            "No active mapping targets this required creation field, so the synchronization pipeline uses its creation default.",
            executeAsync: _ => Task.FromResult(ApplyProductField(product, target, value)));
    }

    private static void AddBooleanAssignment(
        AkeneoExecutableSectionPlan plan,
        Product product,
        string area,
        string target,
        string source,
        bool current,
        bool proposed,
        Action<bool> setter,
        string detail)
    {
        var type = DetermineScalarChangeType(
            current.ToString(),
            proposed.ToString());

        plan.AddOperation(
            area,
            target,
            source,
            current.ToString(),
            proposed.ToString(),
            type,
            detail,
            executeAsync: type == AkeneoDryRunOperationType.NoChange
                ? null
                : _ => Task.FromResult(SetIfChanged(current, proposed, setter)));
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

    private static bool ApplyProductField(
        Product product,
        string targetKey,
        string proposed)
    {
        proposed ??= string.Empty;

        return targetKey switch
        {
            "Name" => SetIfChanged(product.Name, proposed, value => product.Name = value),
            "ShortDescription" => SetIfChanged(product.ShortDescription, proposed, value => product.ShortDescription = value),
            "FullDescription" => SetIfChanged(product.FullDescription, proposed, value => product.FullDescription = value),
            "Sku" => SetIfChanged(product.Sku, proposed, value => product.Sku = value),
            "Price" => decimal.TryParse(
                           proposed,
                           NumberStyles.Number,
                           CultureInfo.InvariantCulture,
                           out var price) &&
                       SetIfChanged(product.Price, price, value => product.Price = value),
            "StockQuantity" => int.TryParse(
                                   proposed,
                                   NumberStyles.Integer,
                                   CultureInfo.InvariantCulture,
                                   out var quantity) &&
                               SetIfChanged(
                                   product.StockQuantity,
                                   quantity,
                                   value => product.StockQuantity = value),
            "Gtin" => SetIfChanged(product.Gtin, proposed, value => product.Gtin = value),
            "ManufacturerPartNumber" => SetIfChanged(
                product.ManufacturerPartNumber,
                proposed,
                value => product.ManufacturerPartNumber = value),
            "Published" => bool.TryParse(proposed, out var published) &&
                           SetIfChanged(
                               product.Published,
                               published,
                               value => product.Published = value),
            _ => false
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

        type = DetermineExactScalarChangeType(current, proposed);
        return true;
    }
}
