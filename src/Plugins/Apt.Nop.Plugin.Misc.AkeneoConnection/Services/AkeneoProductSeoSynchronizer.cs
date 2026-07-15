using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Nop.Core.Domain.Catalog;
using Nop.Services.Catalog;
using Nop.Services.Seo;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
public class AkeneoProductSeoSynchronizer(IProductService productService,
    IUrlRecordService urlRecordService) : IAkeneoProductSectionSynchronizer
{
    private async Task<bool> ApplySeoFieldsAsync(
        Product product,
        IList<AkeneoResolvedMappedValue> mappedValues,
        bool isNew,
        AkeneoMissingValueBehavior missingValueBehavior,
        AkeneoProductImportResult result)
    {
        var changed = false;
        string requestedSeName = null;
        var hasSeNameMapping = false;

        foreach (var mappedValue in mappedValues)
        {
            var targetKey = mappedValue.Mapping.NopTargetKey?.Trim();
            if (string.IsNullOrWhiteSpace(targetKey))
            {
                result.AddWarning($"SEO mapping has no target key. Akeneo attribute: {mappedValue.Mapping.AkeneoAttributeCode}");
                continue;
            }

            if (!mappedValue.HasValue &&
                missingValueBehavior == AkeneoMissingValueBehavior.PreserveExisting)
            {
                continue; // also leaves an existing slug untouched for SeName
            }

            var value = mappedValue.DisplayValue;

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

  

    public async Task SynchronizeAsync(
        AkeneoProductSyncContext context,
        CancellationToken cancellationToken = default)
    {
        if (context.Product is null)
            return;

        var mappings = context
            .GetMappings(NopTargetType.SeoField)
            .ToList();

        var changed = await ApplySeoFieldsAsync(
            context.Product,
            mappings,
            context.ProductCreated,
            context.Request.SeoFieldMissingValueBehavior,
            context.Result);

        if (changed)
            context.MarkChanged();
    }

    public int Order => 200;
}
