using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services.Planning;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Nop.Core.Domain.Catalog;
using static Apt.Nop.Plugin.Misc.AkeneoConnection.Services.DryRun.AkeneoDryRunPlanHelper;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public partial class AkeneoProductSeoSynchronizer
{
    public async Task PlanAsync(
        AkeneoProductSyncContext context,
        AkeneoProductMappingPreviewModel model,
        CancellationToken cancellationToken = default)
    {
        var plan = await BuildPlanAsync(context, model, cancellationToken);
        plan.Render(model);
    }

    private async Task<AkeneoExecutableSectionPlan> BuildPlanAsync(
        AkeneoProductSyncContext context,
        AkeneoProductMappingPreviewModel previewModel,
        CancellationToken cancellationToken)
    {
        var plan = new AkeneoExecutableSectionPlan();
        // Preview and comparison are based on the destination state that existed
        // before this synchronization item started. Attached actions write to
        // context.Product after the Core section has created or updated it.
        var product = context.ExistingProduct;
        var mappings = context.GetMappings(NopTargetType.SeoField).ToList();
        var hasSlugMapping = false;
        var metaChanged = false;
        var hasMetaWriteOperation = false;

        foreach (var mapped in mappings)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var key = mapped.Mapping.NopTargetKey?.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                plan.AddReview(
                    "SEO fields",
                    "Missing target key",
                    GetSourceName(mapped),
                    "The SEO mapping has no nopCommerce target key.",
                    mapped.Mapping.IsRequired);
                continue;
            }

            if (string.Equals(key, "SeName", StringComparison.OrdinalIgnoreCase))
            {
                hasSlugMapping = true;
                await AddSlugOperationAsync(
                    context,
                    plan,
                    previewModel,
                    product,
                    mapped);
                continue;
            }

            var current = key switch
            {
                "MetaTitle" => product?.MetaTitle,
                "MetaDescription" => product?.MetaDescription,
                "MetaKeywords" => product?.MetaKeywords,
                _ => null
            };

            if (key is not ("MetaTitle" or "MetaDescription" or "MetaKeywords"))
            {
                plan.AddReview(
                    "SEO fields",
                    key,
                    GetSourceName(mapped),
                    $"'{key}' is not supported by the current SEO synchronizer.",
                    mapped.Mapping.IsRequired,
                    current,
                    mapped.DisplayValue);
                continue;
            }

            if (!mapped.HasValue &&
                context.Request.SeoFieldMissingValueBehavior ==
                    AkeneoMissingValueBehavior.PreserveExisting)
            {
                plan.AddOperation(
                    "SEO fields",
                    key,
                    GetSourceName(mapped),
                    current,
                    current,
                    AkeneoDryRunOperationType.Preserve,
                    "The mapping produced no value and the profile preserves existing SEO values.",
                    mapped.Mapping.IsRequired);
                continue;
            }

            var proposed = mapped.HasValue ? mapped.DisplayValue : string.Empty;
            var type = DetermineExactScalarChangeType(current, proposed);

            if (context.ExistingProduct == null && type != AkeneoDryRunOperationType.NoChange)
                type = AkeneoDryRunOperationType.Create;

            Func<CancellationToken, Task<bool>> executeAsync = null;
            if (type is AkeneoDryRunOperationType.Create or
                AkeneoDryRunOperationType.Update or
                AkeneoDryRunOperationType.Clear)
            {
                hasMetaWriteOperation = true;
                executeAsync = _ =>
                {
                    if (context.Product == null)
                        return Task.FromResult(false);

                    var changed = key switch
                    {
                        "MetaTitle" => SetIfChanged(
                            context.Product.MetaTitle,
                            proposed,
                            value => context.Product.MetaTitle = value),
                        "MetaDescription" => SetIfChanged(
                            context.Product.MetaDescription,
                            proposed,
                            value => context.Product.MetaDescription = value),
                        "MetaKeywords" => SetIfChanged(
                            context.Product.MetaKeywords,
                            proposed,
                            value => context.Product.MetaKeywords = value),
                        _ => false
                    };

                    metaChanged |= changed;
                    return Task.FromResult(changed);
                };
            }

            plan.AddOperation(
                "SEO fields",
                key,
                GetSourceName(mapped),
                current,
                proposed,
                type,
                null,
                mapped.Mapping.IsRequired,
                executeAsync);
        }

        if (product == null && !hasSlugMapping)
        {
            var proposedName = GetProposedProductName(context, previewModel);
            var slugProduct = new Product { Name = proposedName };
            var proposedSlug = await _urlRecordService.ValidateSeNameAsync(
                slugProduct,
                string.Empty,
                proposedName,
                ensureNotEmpty: true);

            plan.AddOperation(
                "SEO fields",
                "SeName",
                "nopCommerce slug generation",
                null,
                proposedSlug,
                AkeneoDryRunOperationType.Create,
                "New products receive a validated URL slug even when no explicit SeName mapping exists.",
                executeAsync: async _ => await SaveSlugIfChangedAsync(
                    context,
                    proposedSlug));
        }

        if (hasMetaWriteOperation)
        {
            plan.AddInternalOperation(async _ =>
            {
                if (!metaChanged || context.Product == null)
                    return false;

                context.Product.UpdatedOnUtc = DateTime.UtcNow;
                await _productService.UpdateProductAsync(context.Product);
                context.Result.AddMessage(
                    $"Updated SEO meta fields for nopCommerce product ID {context.Product.Id}.");

                return false;
            });
        }

        return plan;
    }

    private async Task AddSlugOperationAsync(
        AkeneoProductSyncContext context,
        AkeneoExecutableSectionPlan plan,
        AkeneoProductMappingPreviewModel previewModel,
        Product product,
        AkeneoResolvedMappedValue mapped)
    {
        var current = product == null || product.Id == 0
            ? null
            : await _urlRecordService.GetActiveSlugAsync(
                product.Id,
                nameof(Product),
                0);

        if (!mapped.HasValue &&
            context.Request.SeoFieldMissingValueBehavior ==
                AkeneoMissingValueBehavior.PreserveExisting &&
            product != null)
        {
            plan.AddOperation(
                "SEO fields",
                "SeName",
                GetSourceName(mapped),
                current,
                current,
                AkeneoDryRunOperationType.Preserve,
                "The mapping produced no value and the profile preserves the existing slug.",
                mapped.Mapping.IsRequired);
            return;
        }

        var proposedName = GetProposedProductName(context, previewModel);
        var slugProduct = new Product
        {
            Id = product?.Id ?? 0,
            Name = proposedName
        };

        var requested = mapped.HasValue ? mapped.DisplayValue : string.Empty;
        var proposed = await _urlRecordService.ValidateSeNameAsync(
            slugProduct,
            requested,
            proposedName,
            ensureNotEmpty: true);

        var type = product == null
            ? AkeneoDryRunOperationType.Create
            : DetermineExactScalarChangeType(current, proposed, ignoreCase: true);

        plan.AddOperation(
            "SEO fields",
            "SeName",
            GetSourceName(mapped),
            current,
            proposed,
            type,
            "The proposed value is the validated slug that nopCommerce would save.",
            mapped.Mapping.IsRequired,
            executeAsync: type is AkeneoDryRunOperationType.Create or
                AkeneoDryRunOperationType.Update or
                AkeneoDryRunOperationType.Clear
                ? async _ => await SaveSlugIfChangedAsync(context, proposed)
                : null);
    }

    private async Task<bool> SaveSlugIfChangedAsync(
        AkeneoProductSyncContext context,
        string proposedSlug)
    {
        if (context.Product == null)
            return false;

        var currentSlug = context.Product.Id == 0
            ? null
            : await _urlRecordService.GetActiveSlugAsync(
                context.Product.Id,
                nameof(Product),
                0);

        if (string.Equals(
                currentSlug,
                proposedSlug,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        await _urlRecordService.SaveSlugAsync(
            context.Product,
            proposedSlug,
            0);
        context.Result.AddMessage(
            $"Set product URL slug '{proposedSlug}'.");

        return true;
    }
}
