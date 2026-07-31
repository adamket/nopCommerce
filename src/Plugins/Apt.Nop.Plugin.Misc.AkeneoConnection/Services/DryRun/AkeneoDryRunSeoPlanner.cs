using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Nop.Core.Domain.Catalog;
using Nop.Services.Seo;
using static Apt.Nop.Plugin.Misc.AkeneoConnection.Services.DryRun.AkeneoDryRunPlanHelper;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services.DryRun;

public sealed class AkeneoDryRunSeoPlanner(
    IUrlRecordService urlRecordService)
    : IAkeneoDryRunSectionPlanner
{
    public int Order => 200;

    public async Task PlanAsync(
        AkeneoProductSyncContext context,
        AkeneoProductMappingPreviewModel model,
        CancellationToken cancellationToken = default)
    {
        var product = context.ExistingProduct;
        var mappings = context.GetMappings(NopTargetType.SeoField).ToList();
        var hasSlugMapping = false;

        foreach (var mapped in mappings)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var key = mapped.Mapping.NopTargetKey?.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                AddReviewOperation(
                    model,
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
                await AddSlugOperationAsync(context, model, product, mapped);
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
                AddReviewOperation(
                    model,
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
                AddOperation(
                    model,
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

            var proposed = mapped.HasValue ? mapped.DisplayValue : null;
            var type = DetermineScalarChangeType(current, proposed);

            if (product == null && type != AkeneoDryRunOperationType.NoChange)
                type = AkeneoDryRunOperationType.Create;

            AddOperation(
                model,
                "SEO fields",
                key,
                GetSourceName(mapped),
                current,
                proposed,
                type,
                null,
                mapped.Mapping.IsRequired);
        }

        if (product != null || hasSlugMapping)
            return;

        var proposedName = GetProposedProductName(context, model);
        var slugProduct = new Product { Name = proposedName };
        var proposedSlug = await urlRecordService.ValidateSeNameAsync(
            slugProduct,
            string.Empty,
            proposedName,
            ensureNotEmpty: true);

        AddOperation(
            model,
            "SEO fields",
            "SeName",
            "nopCommerce slug generation",
            null,
            proposedSlug,
            AkeneoDryRunOperationType.Create,
            "New products receive a validated URL slug even when no explicit SeName mapping exists.");
    }

    private async Task AddSlugOperationAsync(
        AkeneoProductSyncContext context,
        AkeneoProductMappingPreviewModel model,
        Product product,
        AkeneoResolvedMappedValue mapped)
    {
        var current = product == null
            ? null
            : await urlRecordService.GetActiveSlugAsync(
                product.Id,
                nameof(Product),
                0);

        if (!mapped.HasValue &&
            context.Request.SeoFieldMissingValueBehavior ==
                AkeneoMissingValueBehavior.PreserveExisting &&
            product != null)
        {
            AddOperation(
                model,
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

        var proposedName = GetProposedProductName(context, model);
        var slugProduct = new Product
        {
            Id = product?.Id ?? 0,
            Name = proposedName
        };

        var requested = mapped.HasValue ? mapped.DisplayValue : string.Empty;
        var proposed = await urlRecordService.ValidateSeNameAsync(
            slugProduct,
            requested,
            proposedName,
            ensureNotEmpty: true);

        AddOperation(
            model,
            "SEO fields",
            "SeName",
            GetSourceName(mapped),
            current,
            proposed,
            product == null
                ? AkeneoDryRunOperationType.Create
                : DetermineScalarChangeType(current, proposed, ignoreCase: true),
            "The proposed value is the validated slug that nopCommerce would save.",
            mapped.Mapping.IsRequired);
    }
}
