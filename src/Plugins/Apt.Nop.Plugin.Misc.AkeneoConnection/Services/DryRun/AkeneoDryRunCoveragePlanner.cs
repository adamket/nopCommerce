using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using static Apt.Nop.Plugin.Misc.AkeneoConnection.Services.DryRun.AkeneoDryRunPlanHelper;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services.DryRun;

public sealed class AkeneoDryRunCoveragePlanner(
    IAkeneoAssetMappingService assetMappingService,
    IAkeneoFamilyMappingService familyMappingService) : IAkeneoDryRunSectionPlanner
{
    public int Order => 900;

    public async Task PlanAsync(
        AkeneoProductSyncContext context,
        AkeneoProductMappingPreviewModel model,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        AddUnsupportedTargetOperations(context, model);

        var assetMappings = await assetMappingService
            .GetEffectiveMappingsAsync(context.MappingFamilyCode);

        var currentScope = AkeneoAttributeMappingScopeHelper.NormalizeCurrentScope(
            context.MappingEntityScope,
            context.Source,
            context.SourceEntityType);

        var activeAssetMappings = assetMappings
            .Where(mapping =>
                mapping.Enabled &&
                AkeneoAttributeMappingScopeHelper
                    .NormalizeConfiguredScope(mapping.EntityScopeId)
                    .HasFlag(currentScope))
            .OrderBy(mapping => mapping.DisplayOrder)
            .ThenBy(mapping => mapping.Id)
            .ToList();

        if (context.Request.AssetSyncMode != AkeneoCollectionSyncMode.Disabled &&
            activeAssetMappings.Any())
        {
            foreach (var mapping in activeAssetMappings)
            {
                var source = string.Join(
                    " → fallback ",
                    new[]
                    {
                        mapping.SourceAttributeCode?.Trim(),
                        mapping.FallbackSourceAttributeCode?.Trim()
                    }.Where(value => !string.IsNullOrWhiteSpace(value)));

                var mappingName =
                    mapping.Name ?? mapping.MappingKey ?? $"Asset mapping #{mapping.Id}";

                AddReviewOperation(
                    model,
                    "Assets",
                    $"{mappingName} → {(AkeneoAssetDestinationType)mapping.DestinationTypeId}",
                    source,
                    "Media binaries and existing picture/video state are not downloaded during preview, so the final add, replace, reorder, or remove operations are evaluated during import.");
            }

            model.CoverageNotes.Add(
                "Asset mappings are active. The dry run identifies the affected mappings but does not download media binaries, so picture, video, and external-asset replacement details are not itemized.");
        }

        var familyMapping = string.IsNullOrWhiteSpace(context.MappingFamilyCode)
            ? null
            : await familyMappingService.GetByFamilyCodeAsync(context.MappingFamilyCode);

        if (context.SourceEntityType == AkeneoEntityType.Product &&
            !string.IsNullOrWhiteSpace(context.Source.Parent) &&
            familyMapping is { Enabled: true })
        {
            AddReviewOperation(
                model,
                "Variant representation",
                context.Source.Parent,
                "Akeneo family/variant configuration",
                "The import can synchronize the effective parent product model and then apply grouped-product, associated-product, product-attribute-combination, or representation-transition operations. Those branch-level changes are finalized during import.");

            model.CoverageNotes.Add(
                "This source participates in a configured variant hierarchy. Parent product-model changes and the final representation decision are identified for review but are not fully itemized by this leaf preview. Preview the product-model code directly to inspect its mapped parent-product fields.");
        }

        if (context.ExistingSyncState != null &&
            (AkeneoProductLifecycleStatus)context.ExistingSyncState.LifecycleStatusId !=
                AkeneoProductLifecycleStatus.Active)
        {
            model.CoverageNotes.Add(
                "The existing source binding has a non-active lifecycle status. User-visible flag restorations are itemized above; a successful import also returns the internal binding to Active.");
        }

        model.CoverageNotes.Add(
            "The import rebuilds the plan against the then-current nopCommerce state. Changes made after this preview can alter the final result.");
    }

    private static void AddUnsupportedTargetOperations(
        AkeneoProductSyncContext context,
        AkeneoProductMappingPreviewModel model)
    {
        foreach (var mapped in context.MappedValues.Where(value =>
                     value.TargetType == NopTargetType.Manufacturer))
        {
            AddReviewOperation(
                model,
                "Manufacturer",
                mapped.Mapping.NopTargetKey ?? "Manufacturer",
                GetSourceName(mapped),
                "Manufacturer synchronization is not implemented by the current product sync pipeline.",
                mapped.Mapping.IsRequired,
                null,
                mapped.DisplayValue);
        }
    }
}
