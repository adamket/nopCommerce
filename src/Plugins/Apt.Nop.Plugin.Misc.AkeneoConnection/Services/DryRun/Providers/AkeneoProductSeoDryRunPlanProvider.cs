using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services.Planning;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Seo;
using Nop.Data;
using Nop.Services.Seo;
using static Apt.Nop.Plugin.Misc.AkeneoConnection.Services.DryRun.AkeneoDryRunPlanHelper;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public partial class AkeneoProductSeoSynchronizer
{
    public async Task PlanAsync(
        AkeneoProductSyncContext context,
        AkeneoProductMappingPreviewModel model,
        CancellationToken cancellationToken = default)
    {
        var plan = await BuildPlanAsync(context, cancellationToken);
        plan.Render(model);
    }

    private async Task<AkeneoExecutableSectionPlan> BuildPlanAsync(
        AkeneoProductSyncContext context,
        CancellationToken cancellationToken)
    {
        var plan = new AkeneoExecutableSectionPlan();
        // Preview and comparison are based on the destination state that existed
        // before this synchronization item started. Attached actions write to
        // context.Product after the Core section has created or updated it.
        var product = context.ExistingProduct;
        var mappings = context.GetMappings(NopTargetType.SeoField, includeSkipped: true).ToList();
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

            if (mapped.IsSkipped)
            {
                hasSlugMapping |= string.Equals(key, "SeName", StringComparison.OrdinalIgnoreCase);
                plan.AddOperation("SEO fields", key, GetSourceName(mapped), null, null,
                    AkeneoDryRunOperationType.Skip, mapped.ConditionDetail, mapped.Mapping.IsRequired);
                continue;
            }

            if (string.Equals(key, "SeName", StringComparison.OrdinalIgnoreCase))
            {
                hasSlugMapping = true;
                await AddSlugOperationAsync(
                    context,
                    plan,
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
            var proposedName = ResolveProductNameForSeo(context);
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
                // The pipeline publishes the update event once all sections have run.
                await _productRepository.UpdateAsync(context.Product, publishEvent: false);
                context.ProductUpdateEventPending = true;
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

        var proposedName = ResolveProductNameForSeo(context);
        var slugProduct = new Product
        {
            Id = product?.Id ?? 0,
            Name = proposedName
        };

        var requested = mapped.HasValue ? mapped.DisplayValue : string.Empty;
        var reclaim = mapped.HasValue
            ? await FindSlugHeldByDeletedProductsAsync(requested, slugProduct.Id)
            : null;
        var proposed = reclaim?.Slug ?? await _urlRecordService.ValidateSeNameAsync(
            slugProduct,
            requested,
            proposedName,
            ensureNotEmpty: true);

        var type = product == null
            ? AkeneoDryRunOperationType.Create
            : DetermineExactScalarChangeType(current, proposed, ignoreCase: true);

        var adjustmentWarning = mapped.HasValue && reclaim == null
            ? await DescribeSlugAdjustmentAsync(requested, proposed)
            : null;

        if (adjustmentWarning != null)
            plan.Warnings.Add(adjustmentWarning);

        var note = adjustmentWarning ??
                   (reclaim != null
                       ? $"The slug is held by deleted nopCommerce product ID {string.Join(", ", reclaim.DeletedProductIds)} and will be moved to this product."
                       : "The proposed value is the validated slug that nopCommerce would save.");

        plan.AddOperation(
            "SEO fields",
            "SeName",
            GetSourceName(mapped),
            current,
            proposed,
            type,
            note,
            mapped.Mapping.IsRequired,
            executeAsync: type is AkeneoDryRunOperationType.Create or
                AkeneoDryRunOperationType.Update or
                AkeneoDryRunOperationType.Clear
                ? async _ =>
                {
                    if (reclaim != null && context.Product != null)
                        await ReleaseSlugFromDeletedProductsAsync(context, reclaim);

                    return await SaveSlugIfChangedAsync(context, proposed);
                }
                : null);
    }

    private sealed record DeletedProductSlugReclaim(
        string Slug,
        IReadOnlyList<UrlRecord> Records,
        IReadOnlyList<int> DeletedProductIds);

    /// <summary>
    /// Returns the cleaned Akeneo slug when every URL record holding it belongs
    /// to a nopCommerce product that is deleted (or no longer exists), so the
    /// slug can be moved to this product instead of being suffixed with "-2".
    /// Returns null when the slug is free, reserved, or held by a live entity.
    /// </summary>
    private async Task<DeletedProductSlugReclaim> FindSlugHeldByDeletedProductsAsync(
        string requested,
        int productId)
    {
        if (_urlRecordRepository == null || string.IsNullOrWhiteSpace(requested))
            return null;

        var slug = await _urlRecordService.GetSeNameAsync(
            requested.Trim(),
            _seoSettings.ConvertNonWesternChars,
            _seoSettings.AllowUnicodeCharsInUrls);
        slug = CommonHelper.EnsureMaximumLength(slug, NopSeoDefaults.SearchEngineNameLength);

        if (string.IsNullOrWhiteSpace(slug) ||
            _seoSettings.ReservedUrlRecordSlugs?.Contains(slug, StringComparer.InvariantCultureIgnoreCase) == true)
        {
            return null;
        }

        var holders = (await _urlRecordRepository.Table
                .Where(record => record.Slug == slug)
                .ToListAsync())
            .Where(record => record.Slug.Equals(slug, StringComparison.InvariantCultureIgnoreCase) &&
                             !(record.EntityId == productId &&
                               record.EntityName.Equals(nameof(Product), StringComparison.InvariantCultureIgnoreCase)))
            .ToList();

        if (holders.Count == 0 ||
            holders.Any(record => !record.EntityName.Equals(nameof(Product), StringComparison.InvariantCultureIgnoreCase)))
        {
            return null;
        }

        var deletedProductIds = holders.Select(record => record.EntityId).Distinct().ToList();

        foreach (var holderId in deletedProductIds)
        {
            var holder = await _productService.GetProductByIdAsync(holderId);
            if (holder is { Deleted: false })
                return null;
        }

        return new DeletedProductSlugReclaim(slug, holders, deletedProductIds);
    }

    private async Task ReleaseSlugFromDeletedProductsAsync(
        AkeneoProductSyncContext context,
        DeletedProductSlugReclaim reclaim)
    {
        await _urlRecordService.DeleteUrlRecordsAsync(reclaim.Records.ToList());
        context.Result.AddMessage(
            $"Released URL slug '{reclaim.Slug}' from deleted nopCommerce product ID {string.Join(", ", reclaim.DeletedProductIds)}.");
    }

    /// <summary>
    /// Explains why the slug nopCommerce will save differs from the Akeneo
    /// value, or returns null when they match. "Already in use" means
    /// nopCommerce had to add a number to keep the slug unique; otherwise it
    /// only cleaned up characters, spacing or case.
    /// </summary>
    private async Task<string> DescribeSlugAdjustmentAsync(
        string requested,
        string proposed)
    {
        requested = requested?.Trim();

        if (string.IsNullOrWhiteSpace(requested) ||
            string.Equals(requested, proposed, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // The Akeneo value as nopCommerce would clean it, before uniqueness.
        var cleaned = await _urlRecordService.GetSeNameAsync(
            requested,
            _seoSettings.ConvertNonWesternChars,
            _seoSettings.AllowUnicodeCharsInUrls);

        if (!string.IsNullOrWhiteSpace(cleaned) &&
            string.Equals(cleaned, proposed, StringComparison.OrdinalIgnoreCase))
        {
            return $"The Akeneo URL slug '{requested}' was adjusted to '{proposed}' because it contained characters nopCommerce does not allow in URLs.";
        }

        var clashing = string.IsNullOrWhiteSpace(cleaned) ? requested : cleaned;

        return $"The Akeneo URL slug '{requested}' is already in use by another nopCommerce page, so the product uses '{proposed}' instead. " +
               $"It will switch to '{clashing}' automatically once that slug is free.";
    }

    private static string ResolveProductNameForSeo(
        AkeneoProductSyncContext context)
    {
        return !string.IsNullOrWhiteSpace(
                context.PlanningState.ProposedProductName)
            ? context.PlanningState.ProposedProductName
            : context.Product?.Name ??
              context.ExistingProduct?.Name ??
              context.Sku ??
              context.SourceCode ??
              context.ProductKey;
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
