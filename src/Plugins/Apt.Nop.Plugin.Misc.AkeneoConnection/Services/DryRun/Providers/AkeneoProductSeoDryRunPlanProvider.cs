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
            var conflict = await DescribeSlugConflictAsync(
                proposedName,
                proposedSlug,
                fromMapping: false);

            if (conflict != null)
                plan.Errors.Add(conflict);

            plan.AddOperation(
                "SEO fields",
                "SeName",
                "nopCommerce slug generation",
                null,
                proposedSlug,
                AkeneoDryRunOperationType.Create,
                conflict ??
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
            ? await FindReleasableSlugAsync(context, requested, slugProduct.Id)
            : null;
        var proposed = reclaim?.Slug ?? await _urlRecordService.ValidateSeNameAsync(
            slugProduct,
            requested,
            proposedName,
            ensureNotEmpty: true);

        var type = product == null
            ? AkeneoDryRunOperationType.Create
            : DetermineExactScalarChangeType(current, proposed, ignoreCase: true);

        // Without a mapped value nopCommerce builds the slug from the name.
        var conflict = reclaim == null
            ? await DescribeSlugConflictAsync(
                mapped.HasValue ? requested : proposedName,
                proposed,
                fromMapping: mapped.HasValue)
            : null;

        if (conflict != null)
            plan.Errors.Add(conflict);

        var adjustmentWarning = conflict == null && mapped.HasValue && reclaim == null
            ? await DescribeSlugAdjustmentAsync(requested, proposed)
            : null;

        if (adjustmentWarning != null)
            plan.Warnings.Add(adjustmentWarning);

        var note = conflict ?? adjustmentWarning ??
                   (reclaim != null
                       ? $"The slug is held by {reclaim.DescribeHolders()} and will be moved to this product."
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
                        await ReleaseSlugAsync(context, reclaim);

                    var saved = await SaveSlugIfChangedAsync(context, proposed);

                    if (reclaim != null && context.Product != null)
                        await GiveReleasedVariantsSlugsAsync(context, reclaim);

                    return saved;
                }
                : null);
    }

    private sealed record SlugReclaim(
        string Slug,
        IReadOnlyList<UrlRecord> Records,
        IReadOnlyList<int> DeletedProductIds,
        IReadOnlyList<int> VariantProductIds)
    {
        public string DescribeHolders()
        {
            var parts = new List<string>();
            if (DeletedProductIds.Count > 0)
                parts.Add($"deleted nopCommerce product ID {string.Join(", ", DeletedProductIds)}");
            if (VariantProductIds.Count > 0)
                parts.Add($"nopCommerce product ID {string.Join(", ", VariantProductIds)}, a variant of this product model");

            return string.Join(" and ", parts);
        }
    }

    /// <summary>
    /// Returns the cleaned Akeneo slug when every URL record holding it can be
    /// moved to this product instead of the slug being suffixed with "-2":
    /// records of products that are deleted (or no longer exist), and, for a
    /// product-model parent, records of the live products this profile synced
    /// from its own variants. The second case hands a standalone variant's slug
    /// to the parent a sub-model parent rule creates above it. Returns null
    /// when the slug is free, reserved, or held by any other live entity.
    /// </summary>
    private async Task<SlugReclaim> FindReleasableSlugAsync(
        AkeneoProductSyncContext context,
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

        var deletedProductIds = new List<int>();
        var variantProductIds = new List<int>();

        foreach (var holderId in holders.Select(record => record.EntityId).Distinct())
        {
            var holder = await _productService.GetProductByIdAsync(holderId);
            if (holder is not { Deleted: false })
                deletedProductIds.Add(holderId);
            else if (await IsSyncedVariantOfThisModelAsync(context, holderId))
                variantProductIds.Add(holderId);
            else
                return null;
        }

        return new SlugReclaim(slug, holders, deletedProductIds, variantProductIds);
    }

    /// <summary>
    /// True when this profile synced the product from a variant whose Akeneo
    /// parent is the product model being written. Attribute combinations are
    /// excluded: their state points at the parent product itself.
    /// </summary>
    private async Task<bool> IsSyncedVariantOfThisModelAsync(
        AkeneoProductSyncContext context,
        int productId)
    {
        var modelCode = context.SourceCode?.Trim();
        var profileId = context.Request?.SyncProfileId;

        if (_syncStateRepository == null ||
            context.SourceEntityType != AkeneoEntityType.ProductModel ||
            string.IsNullOrWhiteSpace(modelCode) ||
            !profileId.HasValue)
        {
            return false;
        }

        var states = await _syncStateRepository.Table
            .Where(state =>
                state.SyncProfileId == profileId.Value &&
                state.NopProductId == productId &&
                state.AkeneoEntityTypeId == (int)AkeneoEntityType.Product &&
                state.DestinationKindId != (int)AkeneoProductDestinationKind.ProductAttributeCombination)
            .ToListAsync();

        return states.Any(state => string.Equals(
            state.AkeneoParentCode?.Trim(),
            modelCode,
            StringComparison.OrdinalIgnoreCase));
    }

    private async Task ReleaseSlugAsync(
        AkeneoProductSyncContext context,
        SlugReclaim reclaim)
    {
        await _urlRecordService.DeleteUrlRecordsAsync(reclaim.Records.ToList());
        context.Result.AddMessage(
            $"Released URL slug '{reclaim.Slug}' from {reclaim.DescribeHolders()}.");
    }

    /// <summary>
    /// A variant that handed its slug to this parent gets a new one from its
    /// name, numbered by nopCommerce if the name gives the same slug.
    /// </summary>
    private async Task GiveReleasedVariantsSlugsAsync(
        AkeneoProductSyncContext context,
        SlugReclaim reclaim)
    {
        foreach (var productId in reclaim.VariantProductIds)
        {
            var variant = await _productService.GetProductByIdAsync(productId);
            if (variant is not { Deleted: false } ||
                !string.IsNullOrWhiteSpace(await _urlRecordService.GetActiveSlugAsync(productId, nameof(Product), 0)))
            {
                continue;
            }

            var slug = await _urlRecordService.ValidateSeNameAsync(variant, null, variant.Name, true);
            await _urlRecordService.SaveSlugAsync(variant, slug, 0);
            context.Result.AddMessage(
                $"Gave nopCommerce product ID {productId} the URL slug '{slug}' after its old slug moved to this product.");
        }
    }

    /// <summary>
    /// Explains a slug nopCommerce only cleaned up (characters, spacing or
    /// case), or returns null. Numbered slugs are conflicts, reported by
    /// <see cref="DescribeSlugConflictAsync"/>.
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

        var cleaned = await CleanSlugAsync(requested);

        return !string.IsNullOrWhiteSpace(cleaned) &&
               string.Equals(cleaned, proposed, StringComparison.OrdinalIgnoreCase)
            ? $"The Akeneo URL slug '{requested}' was adjusted to '{proposed}' because it contained characters nopCommerce does not allow in URLs."
            : null;
    }

    /// <summary>
    /// Returns an error when nopCommerce would save a numbered slug
    /// (red-maple-2) because the wanted one is taken or reserved, or null.
    /// The product fails instead of going live on a different URL.
    /// </summary>
    private async Task<string> DescribeSlugConflictAsync(
        string source,
        string proposed,
        bool fromMapping)
    {
        var wanted = await CleanSlugAsync(source);

        if (string.IsNullOrWhiteSpace(wanted) ||
            string.Equals(wanted, proposed, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var origin = fromMapping
            ? "Akeneo URL slug"
            : "URL slug generated from the product name";

        return $"The {origin} '{wanted}' is {await DescribeSlugHolderAsync(wanted)}, " +
               $"so nopCommerce would save '{proposed}' instead. The product was not synchronized; " +
               "free the slug or change it in Akeneo, then sync again.";
    }

    private async Task<string> DescribeSlugHolderAsync(string slug)
        {
        var record = await _urlRecordService.GetBySlugAsync(slug);

        if (record == null)
            return "reserved by nopCommerce";

        if (!record.EntityName.Equals(nameof(Product), StringComparison.InvariantCultureIgnoreCase))
            return $"already used by {record.EntityName} ID {record.EntityId}";

        var holder = await _productService.GetProductByIdAsync(record.EntityId);

        return holder == null
            ? $"already used by product ID {record.EntityId}"
            : $"already used by product '{holder.Name}' (ID {holder.Id})";
        }

    /// <summary>
    /// The slug as nopCommerce would clean it, before making it unique.
    /// </summary>
    private async Task<string> CleanSlugAsync(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var slug = await _urlRecordService.GetSeNameAsync(
            value.Trim(),
            _seoSettings.ConvertNonWesternChars,
            _seoSettings.AllowUnicodeCharsInUrls);

        return CommonHelper.EnsureMaximumLength(slug, NopSeoDefaults.SearchEngineNameLength);
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
