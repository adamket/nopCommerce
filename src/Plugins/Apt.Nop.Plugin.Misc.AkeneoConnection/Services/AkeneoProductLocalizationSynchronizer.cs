using System.Linq.Expressions;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services.Planning;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Seo;
using Nop.Services.Localization;
using Nop.Services.Seo;
using static Apt.Nop.Plugin.Misc.AkeneoConnection.Services.DryRun.AkeneoDryRunPlanHelper;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

/// <summary>
/// Writes a product's secondary-locale values (for example es_US) as nopCommerce
/// localized values for the matching language: name, descriptions, meta fields
/// and a per-language slug. The primary locale keeps the base fields. A value
/// Akeneo no longer has is removed, so the storefront shows the primary value.
/// </summary>
public class AkeneoProductLocalizationSynchronizer(
    ILocalizedEntityService localizedEntityService,
    IUrlRecordService urlRecordService,
    SeoSettings seoSettings = null)
    : IAkeneoProductSectionSynchronizer,
      IAkeneoSectionDryRunPlanProvider
{
    private readonly SeoSettings _seoSettings = seoSettings ?? new SeoSettings();

    // After core (100) and SEO (200), so the product and its primary slug exist.
    public int Order => 250;

    public async Task SynchronizeAsync(
        AkeneoProductSyncContext context,
        CancellationToken cancellationToken = default)
    {
        if (context.Product is null || context.LocalizedValues.Count == 0)
            return;

        var plan = await BuildPlanAsync(context, cancellationToken);
        await plan.ExecuteAsync(context, cancellationToken);
    }

    public async Task PlanAsync(
        AkeneoProductSyncContext context,
        AkeneoProductMappingPreviewModel model,
        CancellationToken cancellationToken = default)
    {
        if (context.LocalizedValues.Count == 0)
            return;

        var plan = await BuildPlanAsync(context, cancellationToken);
        plan.Render(model);
    }

    private async Task<AkeneoExecutableSectionPlan> BuildPlanAsync(
        AkeneoProductSyncContext context,
        CancellationToken cancellationToken)
    {
        var plan = new AkeneoExecutableSectionPlan();
        var productId = context.ExistingProduct?.Id ?? 0;

        foreach (var localized in context.LocalizedValues)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.Equals(localized.TargetKey, "SeName", StringComparison.OrdinalIgnoreCase))
            {
                await AddSlugOperationAsync(context, plan, productId, localized);
                continue;
            }

            var current = productId == 0
                ? null
                : await localizedEntityService.GetLocalizedValueAsync(
                    localized.LanguageId, productId, nameof(Product), localized.TargetKey);
            var proposed = localized.Value ?? string.Empty;
            var type = DetermineExactScalarChangeType(current ?? string.Empty, proposed);

            if (type == AkeneoDryRunOperationType.NoChange && localized.Value == null)
                continue;

            plan.AddOperation(
                "Translations",
                $"{localized.TargetKey} ({localized.LanguageName})",
                $"{localized.SourceName} [{localized.Locale}]",
                current,
                proposed,
                type,
                localized.Value == null
                    ? $"Akeneo has no {localized.Locale} value, so the {localized.LanguageName} value is removed and the primary value shows."
                    : null,
                executeAsync: type is AkeneoDryRunOperationType.Create or AkeneoDryRunOperationType.Update or AkeneoDryRunOperationType.Clear
                    ? async _ =>
                    {
                        await localizedEntityService.SaveLocalizedValueAsync(
                            context.Product, Selector(localized.TargetKey), localized.Value ?? string.Empty, localized.LanguageId);
                        return true;
                    }
                    : null);
        }

        return plan;
    }

    /// <summary>
    /// A per-language slug. Slugs are unique across all languages, so a taken
    /// one is skipped with a warning instead of failing the product.
    /// </summary>
    private async Task AddSlugOperationAsync(
        AkeneoProductSyncContext context,
        AkeneoExecutableSectionPlan plan,
        int productId,
        AkeneoLocalizedValue localized)
    {
        var current = productId == 0
            ? null
            : await urlRecordService.GetActiveSlugAsync(productId, nameof(Product), localized.LanguageId);

        string proposed = null;
        if (localized.Value != null)
        {
            var cleaned = CommonHelper.EnsureMaximumLength(
                await urlRecordService.GetSeNameAsync(
                    localized.Value.Trim(),
                    _seoSettings.ConvertNonWesternChars,
                    _seoSettings.AllowUnicodeCharsInUrls),
                NopSeoDefaults.SearchEngineNameLength);

            if (!string.IsNullOrWhiteSpace(cleaned))
            {
                var validated = await urlRecordService.ValidateSeNameAsync(
                    productId, nameof(Product), cleaned, null, false);

                if (!string.Equals(cleaned, validated, StringComparison.OrdinalIgnoreCase))
                {
                    plan.Warnings.Add(
                        $"The {localized.LanguageName} URL slug '{cleaned}' is already used or reserved, so the {localized.LanguageName} slug was left unchanged.");
                    return;
                }

                proposed = cleaned;
            }
        }

        var type = DetermineExactScalarChangeType(current ?? string.Empty, proposed ?? string.Empty, ignoreCase: true);
        if (type == AkeneoDryRunOperationType.NoChange)
            return;

        plan.AddOperation(
            "Translations",
            $"SeName ({localized.LanguageName})",
            $"{localized.SourceName} [{localized.Locale}]",
            current,
            proposed ?? string.Empty,
            type,
            proposed == null
                ? $"Akeneo has no {localized.Locale} slug, so the {localized.LanguageName} slug is removed and the primary slug is used."
                : null,
            executeAsync: async _ =>
            {
                await urlRecordService.SaveSlugAsync(context.Product, proposed ?? string.Empty, localized.LanguageId);
                return true;
            });
    }

    private static Expression<Func<Product, string>> Selector(string targetKey) => targetKey.ToLowerInvariant() switch
    {
        "name" => product => product.Name,
        "shortdescription" => product => product.ShortDescription,
        "fulldescription" => product => product.FullDescription,
        "metatitle" => product => product.MetaTitle,
        "metadescription" => product => product.MetaDescription,
        "metakeywords" => product => product.MetaKeywords,
        _ => throw new ArgumentOutOfRangeException(nameof(targetKey), targetKey, "Not a localizable product property.")
    };
}
