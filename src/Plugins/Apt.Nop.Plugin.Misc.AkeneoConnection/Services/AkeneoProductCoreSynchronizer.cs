using System.Globalization;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Nop.Core.Domain.Catalog;
using Nop.Services.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public class AkeneoProductCoreSynchronizer(
    IProductService productService)
    : IAkeneoProductSectionSynchronizer
{
    public int Order => 100;

    public async Task SynchronizeAsync(
        AkeneoProductSyncContext context,
        CancellationToken cancellationToken = default)
    {
        var product = context.ExistingProduct;
        var isNew = product == null;

        if (isNew && !context.Request.CreateNewProducts)
        {
            context.StopProcessing = true;
            context.Result.AddMessage(
                "Product does not exist and creation is disabled.");
            return;
        }

        if (!isNew && !context.Request.UpdateExistingProducts)
        {
            context.Product = product;
            context.StopProcessing = true;
            context.Result.NopProductId = product.Id;
            context.Result.AddMessage(
                $"Product {product.Id} exists and updates are disabled.");
            return;
        }

        product ??= CreateBaseProduct(context);

        var changed = RestoreManagedLifecycleIfNeeded(
            context,
            product);

        changed |= RestoreProductRepresentationIfNeeded(
            context,
            product);

        changed |= ApplyProductFields(
            context,
            product);

        if (isNew)
        {
            product.CreatedOnUtc = DateTime.UtcNow;
            product.UpdatedOnUtc = DateTime.UtcNow;

            await productService.InsertProductAsync(product);

            context.ProductCreated = true;
            context.MarkChanged();
            context.Result.AddMessage(
                $"Created nopCommerce product ID {product.Id}.");
        }
        else if (changed)
        {
            product.UpdatedOnUtc = DateTime.UtcNow;

            await productService.UpdateProductAsync(product);

            context.MarkChanged();
            context.Result.AddMessage(
                $"Updated core product fields for product ID {product.Id}.");
        }

        context.Product = product;
        context.Result.NopProductId = product.Id;
    }

    private static Product CreateBaseProduct(
        AkeneoProductSyncContext context)
    {
        var fallbackName =
            context.Sku ??
            context.SourceCode ??
            context.ProductKey;

        return new Product
        {
            ProductType = ProductType.SimpleProduct,
            VisibleIndividually = true,
            Sku = fallbackName,
            Name = fallbackName,
            Published = context.Source.Enabled ?? false,
            CreatedOnUtc = DateTime.UtcNow,
            UpdatedOnUtc = DateTime.UtcNow
        };
    }

    private static bool RestoreManagedLifecycleIfNeeded(
        AkeneoProductSyncContext context,
        Product product)
    {
        if (context.ExistingSyncState == null)
            return false;

        var lifecycleStatus =
            (AkeneoProductLifecycleStatus)context.ExistingSyncState.LifecycleStatusId;

        if (lifecycleStatus == AkeneoProductLifecycleStatus.Active)
            return false;

        var changed = false;

        if (lifecycleStatus == AkeneoProductLifecycleStatus.PurchasingDisabled)
        {
            changed |= AkeneoMappingHelper.SetIfChanged(
                product.DisableBuyButton,
                false,
                value => product.DisableBuyButton = value);
        }

        if (lifecycleStatus == AkeneoProductLifecycleStatus.SoftDeleted)
        {
            changed |= AkeneoMappingHelper.SetIfChanged(
                product.Deleted,
                false,
                value => product.Deleted = value);
        }

        var wasPublicationChangedByLifecycle =
            lifecycleStatus is AkeneoProductLifecycleStatus.Unpublished or
                AkeneoProductLifecycleStatus.SoftDeleted;

        var hasPublishedMapping = context
            .GetMappings(NopTargetType.ProductField)
            .Any(mapped => string.Equals(
                mapped.Mapping.NopTargetKey,
                "Published",
                StringComparison.OrdinalIgnoreCase));

        if (wasPublicationChangedByLifecycle && !hasPublishedMapping)
        {
            changed |= AkeneoMappingHelper.SetIfChanged(
                product.Published,
                context.Source.Enabled ?? true,
                value => product.Published = value);
        }

        return changed;
    }

    private static bool RestoreProductRepresentationIfNeeded(
        AkeneoProductSyncContext context,
        Product product)
    {
        if (context.ExistingSyncState == null ||
            (AkeneoProductDestinationKind)context.ExistingSyncState.DestinationKindId !=
                AkeneoProductDestinationKind.ProductAttributeCombination ||
            product.Id == context.ExistingSyncState.NopProductId)
        {
            return false;
        }

        var changed = false;

        // The previous combination representation may have hidden and disabled
        // an older child product. Re-enable that child before applying the new
        // grouped/associated/standalone representation.
        changed |= AkeneoMappingHelper.SetIfChanged(
            product.DisableBuyButton,
            false,
            value => product.DisableBuyButton = value);

        var hasPublishedMapping = context
            .GetMappings(NopTargetType.ProductField)
            .Any(mapped => string.Equals(
                mapped.Mapping.NopTargetKey,
                "Published",
                StringComparison.OrdinalIgnoreCase));

        if (!hasPublishedMapping && context.Source.Enabled.HasValue)
        {
            changed |= AkeneoMappingHelper.SetIfChanged(
                product.Published,
                context.Source.Enabled.Value,
                value => product.Published = value);
        }

        return changed;
    }

    private static bool ApplyProductFields(
        AkeneoProductSyncContext context,
        Product product)
    {
        var changed = false;

        foreach (var mapped in context.GetMappings(
                     NopTargetType.ProductField))
        {
            var targetKey = mapped.Mapping.NopTargetKey?.Trim();

            if (string.IsNullOrWhiteSpace(targetKey))
            {
                context.Result.AddWarning(
                    $"Product field mapping has no target key. " +
                    $"Akeneo attribute: {mapped.Mapping.AkeneoAttributeCode}");

                continue;
            }

            if (!mapped.HasValue &&
                context.Request.ProductFieldMissingValueBehavior ==
                    AkeneoMissingValueBehavior.PreserveExisting)
            {
                continue;
            }

            var value = mapped.HasValue
                ? mapped.DisplayValue
                : string.Empty;

            switch (targetKey)
            {
                case "Name":
                    // Do not leave nopCommerce's required product name empty.
                    var desiredName = mapped.HasValue
                        ? value
                        : context.ProductKey;

                    changed |= AkeneoMappingHelper.SetIfChanged(
                        product.Name,
                        desiredName,
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
                    // SKU is an identity field. Preserve it when Akeneo is empty.
                    if (mapped.HasValue)
                    {
                        changed |= AkeneoMappingHelper.SetIfChanged(
                            product.Sku,
                            value,
                            newValue => product.Sku = newValue);
                    }

                    break;

                case "Price":
                    if (!mapped.HasValue)
                    {
                        changed |= AkeneoMappingHelper.SetIfChanged(
                            product.Price,
                            0m,
                            newValue => product.Price = newValue);
                    }
                    else if (TryParseDecimal(value, out var price))
                    {
                        changed |= AkeneoMappingHelper.SetIfChanged(
                            product.Price,
                            price,
                            newValue => product.Price = newValue);
                    }
                    else
                    {
                        context.Result.AddWarning(
                            $"Could not parse price value '{value}' from " +
                            $"{mapped.Mapping.AkeneoAttributeCode}.");
                    }

                    break;

                case "StockQuantity":
                    if (!mapped.HasValue)
                    {
                        changed |= AkeneoMappingHelper.SetIfChanged(
                            product.StockQuantity,
                            0,
                            newValue => product.StockQuantity = newValue);
                    }
                    else if (int.TryParse(
                                 value,
                                 NumberStyles.Integer,
                                 CultureInfo.InvariantCulture,
                                 out var stockQuantity))
                    {
                        changed |= AkeneoMappingHelper.SetIfChanged(
                            product.StockQuantity,
                            stockQuantity,
                            newValue => product.StockQuantity = newValue);
                    }
                    else
                    {
                        context.Result.AddWarning(
                            $"Could not parse stock quantity '{value}' from " +
                            $"{mapped.Mapping.AkeneoAttributeCode}.");
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
                    if (!mapped.HasValue)
                    {
                        changed |= AkeneoMappingHelper.SetIfChanged(
                            product.Published,
                            false,
                            newValue => product.Published = newValue);
                    }
                    else if (TryParseBoolean(value, out var published))
                    {
                        changed |= AkeneoMappingHelper.SetIfChanged(
                            product.Published,
                            published,
                            newValue => product.Published = newValue);
                    }
                    else
                    {
                        context.Result.AddWarning(
                            $"Could not parse published value '{value}' from " +
                            $"{mapped.Mapping.AkeneoAttributeCode}.");
                    }

                    break;

                default:
                    context.Result.AddWarning(
                        $"Unsupported product field target key: {targetKey}");
                    break;
            }
        }

        return changed;
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
}