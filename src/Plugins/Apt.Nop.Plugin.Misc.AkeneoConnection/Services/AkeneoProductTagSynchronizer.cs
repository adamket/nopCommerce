using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services.Planning;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

/// <summary>
/// Syncs nopCommerce product tags from attributes mapped to <see cref="NopTargetType.ProductTag"/>.
/// A select or multi-select contributes its option labels (request locale, then en_US); an
/// option with no label is reported and contributes nothing, as for specification options. A
/// text value contributes its comma-separated words. Tags are matched by name ignoring case,
/// and a missing tag is created. The profile's tag sync mode works like the category mode:
/// Merge only adds, Replace managed also removes links this plugin created that are no longer
/// wanted, and Replace all makes the product's tags match Akeneo exactly. No tag is removed
/// while the wanted set is incomplete.
/// </summary>
public partial class AkeneoProductTagSynchronizer
    : IAkeneoProductSectionSynchronizer,
      IAkeneoSectionDryRunPlanProvider
{
    private const string Area = "Product tags";

    /// <summary>nopCommerce's limit on a tag name.</summary>
    internal const int MaxTagNameLength = 400;

    private readonly IAkeneoProductTagStore _tagStore;
    private readonly IAkeneoManagedRelationService _managedRelationService;
    private readonly IAkeneoApiClient _akeneoApiClient;

    // Per sync scope: attribute code -> option code -> Akeneo option, read once per attribute.
    private readonly Dictionary<string, Dictionary<string, AkeneoAttributeOptionDefinition>>
        _attributeOptions = new(StringComparer.OrdinalIgnoreCase);

    public AkeneoProductTagSynchronizer(
        IAkeneoProductTagStore tagStore,
        IAkeneoManagedRelationService managedRelationService,
        IAkeneoApiClient akeneoApiClient = null)
    {
        _tagStore = tagStore;
        _managedRelationService = managedRelationService;
        _akeneoApiClient = akeneoApiClient;
    }

    public int Order => 350;

    public async Task SynchronizeAsync(
        AkeneoProductSyncContext context,
        CancellationToken cancellationToken = default)
    {
        var plan = await BuildPlanAsync(context, cancellationToken);
        await plan.ExecuteAsync(context, cancellationToken);
    }

    public async Task PlanAsync(
        AkeneoProductSyncContext context,
        AkeneoProductMappingPreviewModel model,
        CancellationToken cancellationToken = default)
    {
        var plan = await BuildPlanAsync(context, cancellationToken);
        plan.Render(model);
    }

    /// <summary>The comma-separated tag names in a text value.</summary>
    internal static IEnumerable<string> SplitTextTags(string text) =>
        (text ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private async Task<AkeneoExecutableSectionPlan> BuildPlanAsync(
        AkeneoProductSyncContext context,
        CancellationToken cancellationToken)
    {
        var plan = new AkeneoExecutableSectionPlan();
        if (context.MappedValues.Any(mapped => mapped.TargetType == NopTargetType.ProductTag && mapped.IsSkipped))
        {
            plan.AddOperation("Product tags", "Collection", null, null, null, AkeneoDryRunOperationType.Skip,
                "A mapping was skipped. The collection is preserved because its complete desired membership is unknown.");
            return plan;
        }
        var mode = context.Request.TagSyncMode;

        if (mode == AkeneoCollectionSyncMode.Disabled)
        {
            plan.CoverageNotes.Add(
                "Product tag synchronization is disabled by the saved sync profile.");
            return plan;
        }

        var product = context.Product ?? context.ExistingProduct;

        // Tag name (ignoring case) -> the Akeneo source that asked for it.
        var desired = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var desiredStateComplete = true;

        foreach (var mapped in context
                     .GetMappings(NopTargetType.ProductTag)
                     .Where(value => value.HasValue))
        {
            if (AkeneoOptionLabels.IsOptionSource(mapped))
            {
                var attributeCode = mapped.Value.AttributeCode;

                foreach (var optionItem in AkeneoSyncValueHelper.GetOptionItems(mapped))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var label = await AkeneoOptionLabels.ResolveAsync(
                        _akeneoApiClient,
                        _attributeOptions,
                        attributeCode,
                        optionItem,
                        context.Request.Locale,
                        cancellationToken);

                    var source = $"{attributeCode}: {optionItem.AkeneoOptionCode}";

                    if (label == null)
                    {
                        desiredStateComplete = false;
                        plan.AddReview(
                            Area,
                            optionItem.AkeneoOptionCode,
                            source,
                            $"Akeneo option '{optionItem.AkeneoOptionCode}' of '{attributeCode}' has no label, so no product tag was assigned for it.");
                        continue;
                    }

                    AddDesired(plan, desired, label, source, ref desiredStateComplete);
                }

                continue;
            }

            foreach (var text in AkeneoSyncValueHelper.GetRawItems(mapped))
            {
                foreach (var name in SplitTextTags(text))
                    AddDesired(plan, desired, name, mapped.ResolvedSourceDisplayName, ref desiredStateComplete);
            }
        }

        var currentLinks = product == null
            ? new List<AkeneoProductTagLink>()
            : await _tagStore.GetProductTagLinksAsync(product.Id);

        var currentNames = currentLinks
            .Select(link => link.Tag.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, source) in desired)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (currentNames.Contains(name))
            {
                plan.AddOperation(Area, name, source, "Assigned", "Assigned", AkeneoDryRunOperationType.NoChange);
                continue;
            }

            var existingTag = await _tagStore.FindTagByNameAsync(name);

            plan.AddOperation(
                Area,
                name,
                source,
                "Not assigned",
                "Assigned",
                AkeneoDryRunOperationType.Add,
                existingTag == null ? "The tag does not exist yet and is created." : null,
                executeAsync: async _ =>
                {
                    if (product == null)
                        return false;

                    var tag = await _tagStore.FindTagByNameAsync(name) ??
                              await _tagStore.CreateTagAsync(name);
                    var link = await _tagStore.AddLinkAsync(product.Id, tag.Id);

                    if (context.Request.SyncProfileId.HasValue)
                    {
                        await _managedRelationService.UpsertAsync(
                            context.Request.SyncProfileId.Value,
                            context.Request.SyncRunRecordId,
                            product.Id,
                            AkeneoManagedRelationType.ProductTag,
                            link.Id,
                            akeneoValueCode: tag.Name);
                    }

                    return true;
                });
        }

        if (mode == AkeneoCollectionSyncMode.Merge)
            return plan;

        if (!desiredStateComplete)
        {
            plan.Warnings.Add(
                "Product tag removal was skipped because the complete desired tag state could not be resolved.");
            plan.CoverageNotes.Add(
                "Product tag removals are not planned because the complete desired tag state could not be resolved. The real synchronizer executes this same plan.");
            return plan;
        }

        if (mode == AkeneoCollectionSyncMode.ReplaceAll)
        {
            foreach (var stale in currentLinks.Where(link => !desired.ContainsKey(link.Tag.Name)))
            {
                plan.AddOperation(
                    Area,
                    stale.Tag.Name,
                    null,
                    "Assigned",
                    "Removed",
                    AkeneoDryRunOperationType.Remove,
                    "Replace-all makes the product's tags match Akeneo.",
                    executeAsync: async _ =>
                    {
                        await _tagStore.RemoveLinkAsync(stale.Mapping);
                        return true;
                    });
            }

            return plan;
        }

        if (!context.Request.SyncProfileId.HasValue || product == null)
        {
            plan.Warnings.Add(
                "Replace-managed product tag synchronization requires a saved sync profile. No tags were removed.");
            plan.CoverageNotes.Add(
                "Replace-managed product tag removals require a saved sync profile and cannot be planned without one.");
            return plan;
        }

        var managedRelations = await _managedRelationService.GetByProductAsync(
            context.Request.SyncProfileId.Value,
            product.Id,
            AkeneoManagedRelationType.ProductTag);

        foreach (var relation in managedRelations)
        {
            var current = currentLinks.FirstOrDefault(link =>
                link.Mapping.Id == relation.NopRelationEntityId);

            if (current == null)
            {
                // The link was removed outside the plugin; forget it.
                plan.AddInternalOperation(async _ =>
                {
                    await _managedRelationService.DeleteAsync(relation);
                    return false;
                });
                continue;
            }

            if (desired.ContainsKey(current.Tag.Name))
                continue;

            plan.AddOperation(
                Area,
                current.Tag.Name,
                relation.AkeneoValueCode,
                "Assigned",
                "Removed",
                AkeneoDryRunOperationType.Remove,
                "This tag link is owned by the plugin and is stale for this profile.",
                executeAsync: async _ =>
                {
                    await _tagStore.RemoveLinkAsync(current.Mapping);
                    await _managedRelationService.DeleteAsync(relation);
                    return true;
                });
        }

        return plan;
    }

    private static void AddDesired(
        AkeneoExecutableSectionPlan plan,
        Dictionary<string, string> desired,
        string name,
        string source,
        ref bool desiredStateComplete)
    {
        name = name?.Trim();

        if (string.IsNullOrEmpty(name))
            return;

        if (name.Length > MaxTagNameLength)
        {
            desiredStateComplete = false;
            plan.AddReview(
                Area,
                name[..40] + "…",
                source,
                $"A product tag name is limited to {MaxTagNameLength} characters, so this one was not assigned.");
            return;
        }

        desired.TryAdd(name, source);
    }
}
