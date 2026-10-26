using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services.Planning;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Services.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

/// <summary>One Akeneo association target: a product model code, or a product identifier or UUID.</summary>
public sealed record AkeneoRelatedProductTarget(AkeneoEntityType EntityType, string Reference)
{
    public string Display => EntityType == AkeneoEntityType.ProductModel
        ? $"product model {Reference}"
        : $"product {Reference}";
}

/// <summary>Everything needed to (re)apply one product's association links outside its sync context.</summary>
public sealed record AkeneoRelatedProductWork(
    int ProductId,
    string SourceCode,
    IReadOnlyList<AkeneoRelatedProductTarget> Targets,
    AkeneoCollectionSyncMode Mode,
    int? SyncProfileId,
    int SyncRunRecordId);

public sealed record AkeneoRelatedProductRetryOutcome(int Added, int Removed, int StillUnresolved);

/// <summary>
/// Syncs one kind of nopCommerce product-to-product link (related products, cross-sells) from
/// one Akeneo association type set on the sync profile. Only the products a product model or a
/// standalone product becomes carry them; variant leaves are skipped, because Akeneo already
/// reports a model's associations on its variants. Targets are resolved through the plugin's
/// bindings: a product model to its nopCommerce product, a product to its own nopCommerce
/// product, or to the parent product when it is represented as an attribute combination. A
/// target that is not in nopCommerce yet is reported and, during a batch run, retried once every
/// item has been processed (<see cref="RetryAsync"/>). The profile's mode works like the category
/// mode, and no link is removed while any target is unresolved.
/// </summary>
public abstract class AkeneoProductAssociationSynchronizer
    : IAkeneoProductSectionSynchronizer,
      IAkeneoSectionDryRunPlanProvider
{
    /// <summary>One existing nopCommerce link from a product to <paramref name="TargetProductId"/>.</summary>
    protected sealed record AssociationLink(int Id, int TargetProductId, int DisplayOrder, BaseEntity Entity);

    protected readonly IProductService ProductService;
    private readonly IAkeneoNopEntityMappingService _entityMappingService;
    private readonly IAkeneoProductSyncStateService _syncStateService;
    private readonly IAkeneoManagedRelationService _managedRelationService;
    private readonly IProductAttributeService _productAttributeService;

    // Products from this sync scope with targets that were not in nopCommerce yet.
    private readonly List<AkeneoRelatedProductWork> _pending = new();

    protected AkeneoProductAssociationSynchronizer(
        IProductService productService,
        IAkeneoNopEntityMappingService entityMappingService,
        IAkeneoProductSyncStateService syncStateService,
        IAkeneoManagedRelationService managedRelationService,
        IProductAttributeService productAttributeService)
    {
        ProductService = productService;
        _entityMappingService = entityMappingService;
        _syncStateService = syncStateService;
        _managedRelationService = managedRelationService;
        _productAttributeService = productAttributeService;
    }

    public abstract int Order { get; }

    /// <summary>The Dry Run area and plural name, e.g. "Related products".</summary>
    public abstract string Area { get; }

    /// <summary>One link in prose, e.g. "related product".</summary>
    protected abstract string Noun { get; }

    /// <summary>The value shown for a linked target, e.g. "Related"; it also prefixes target names in messages.</summary>
    protected abstract string LinkedValue { get; }

    /// <summary>The value shown for a target that is not linked yet, e.g. "Not related".</summary>
    protected abstract string NotLinkedValue { get; }

    protected abstract AkeneoManagedRelationType RelationType { get; }

    protected abstract AkeneoCollectionSyncMode GetMode(AkeneoProductImportRequest request);

    protected abstract string GetAssociationType(AkeneoProductImportRequest request);

    protected abstract Task<IList<AssociationLink>> GetLinksAsync(int productId);

    /// <returns>The new link's ID.</returns>
    protected abstract Task<int> InsertLinkAsync(int productId, int targetProductId, int displayOrder);

    protected abstract Task DeleteLinkAsync(AssociationLink link);

    private string CapitalNoun => char.ToUpperInvariant(Noun[0]) + Noun[1..];

    public async Task SynchronizeAsync(
        AkeneoProductSyncContext context,
        CancellationToken cancellationToken = default)
    {
        var (plan, work, unresolved) = await BuildContextPlanAsync(context, cancellationToken);
        await plan.ExecuteAsync(context, cancellationToken);

        if (work != null && unresolved > 0)
        {
            var product = context.Product ?? context.ExistingProduct;
            if (product?.Id > 0)
                _pending.Add(work with { ProductId = product.Id });
        }
    }

    public async Task PlanAsync(
        AkeneoProductSyncContext context,
        AkeneoProductMappingPreviewModel model,
        CancellationToken cancellationToken = default)
    {
        var (plan, _, _) = await BuildContextPlanAsync(context, cancellationToken);
        plan.Render(model);
    }

    /// <summary>Returns, and forgets, the products waiting for a retry.</summary>
    public IReadOnlyList<AkeneoRelatedProductWork> TakePending()
    {
        var pending = _pending.ToList();
        _pending.Clear();
        return pending;
    }

    /// <summary>
    /// Re-applies one product's links once the rest of the run has been processed, so targets
    /// synced after their source are linked in the same run.
    /// </summary>
    public async Task<AkeneoRelatedProductRetryOutcome> RetryAsync(
        AkeneoRelatedProductWork work,
        CancellationToken cancellationToken = default)
    {
        var product = await ProductService.GetProductByIdAsync(work.ProductId);
        if (product == null || product.Deleted)
            return new AkeneoRelatedProductRetryOutcome(0, 0, 0);

        var plan = new AkeneoExecutableSectionPlan();
        var unresolved = await AddWorkPlanAsync(plan, work, product, cancellationToken);

        var added = 0;
        var removed = 0;

        foreach (var operation in plan.Operations.Where(operation => operation.ExecuteAsync != null))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!await operation.ExecuteAsync(cancellationToken))
                continue;

            if (operation.Preview?.ChangeType == AkeneoDryRunOperationType.Add)
                added++;
            else if (operation.Preview?.ChangeType == AkeneoDryRunOperationType.Remove)
                removed++;
        }

        return new AkeneoRelatedProductRetryOutcome(added, removed, unresolved);
    }

    private async Task<(AkeneoExecutableSectionPlan Plan, AkeneoRelatedProductWork Work, int Unresolved)>
        BuildContextPlanAsync(
            AkeneoProductSyncContext context,
            CancellationToken cancellationToken)
    {
        var plan = new AkeneoExecutableSectionPlan();
        var request = context.Request;
        var mode = GetMode(request);

        if (mode == AkeneoCollectionSyncMode.Disabled)
        {
            plan.CoverageNotes.Add(
                $"{CapitalNoun} synchronization is disabled by the saved sync profile.");
            return (plan, null, 0);
        }

        // A variant inherits its model's associations; the model's product carries them.
        if (context.SourceEntityType == AkeneoEntityType.Product &&
            !string.IsNullOrWhiteSpace(context.Source?.Parent))
        {
            return (plan, null, 0);
        }

        var associationType = GetAssociationType(request)?.Trim();

        if (string.IsNullOrEmpty(associationType))
        {
            plan.Warnings.Add(
                $"{CapitalNoun} synchronization is on, but the sync profile has no association type. {Area} were left unchanged.");
            return (plan, null, 0);
        }

        // Akeneo reports every association type on every product, so a missing one means the
        // code is wrong; it must never read as "no links" and remove them all.
        if (context.Source?.Associations == null ||
            !context.Source.Associations.TryGetValue(associationType, out var association))
        {
            plan.AddReview(
                Area,
                associationType,
                associationType,
                $"Akeneo returned no '{associationType}' association for {context.SourceCode}. Check the association type on the sync profile; {Area.ToLowerInvariant()} were left unchanged.");
            return (plan, null, 0);
        }

        var targets = (association.ProductModels ?? new List<string>())
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => new AkeneoRelatedProductTarget(AkeneoEntityType.ProductModel, code.Trim()))
            .Concat((association.Products ?? new List<string>())
                .Where(reference => !string.IsNullOrWhiteSpace(reference))
                .Select(reference => new AkeneoRelatedProductTarget(AkeneoEntityType.Product, reference.Trim())))
            .Distinct()
            .ToList();

        var product = context.Product ?? context.ExistingProduct;
        var work = new AkeneoRelatedProductWork(
            product?.Id ?? 0,
            context.SourceCode,
            targets,
            mode,
            request.SyncProfileId,
            request.SyncRunRecordId);

        var unresolved = await AddWorkPlanAsync(plan, work, product, cancellationToken);
        return (plan, work, unresolved);
    }

    /// <returns>How many targets could not be resolved.</returns>
    private async Task<int> AddWorkPlanAsync(
        AkeneoExecutableSectionPlan plan,
        AkeneoRelatedProductWork work,
        Product product,
        CancellationToken cancellationToken)
    {
        var desired = new Dictionary<int, (AkeneoRelatedProductTarget Target, Product Product)>();
        var unresolved = 0;

        foreach (var target in work.Targets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var nopProductId = await ResolveAsync(target, work.SyncProfileId);

            if (!nopProductId.HasValue)
            {
                unresolved++;
                plan.AddReview(
                    Area,
                    target.Reference,
                    target.Display,
                    $"{LinkedValue} {target.Display} is not in nopCommerce yet (not synced or bound), so it was not linked.");
                continue;
            }

            if (product != null && nopProductId.Value == product.Id)
                continue;

            var targetProduct = await ProductService.GetProductByIdAsync(nopProductId.Value);

            if (targetProduct == null || targetProduct.Deleted)
            {
                unresolved++;
                plan.AddReview(
                    Area,
                    target.Reference,
                    target.Display,
                    $"{LinkedValue} {target.Display} maps to missing or deleted nopCommerce product ID {nopProductId.Value}, so it was not linked.");
                continue;
            }

            desired.TryAdd(targetProduct.Id, (target, targetProduct));
        }

        var current = product == null
            ? new List<AssociationLink>()
            : await GetLinksAsync(product.Id);

        var currentTargets = current.Select(link => link.TargetProductId).ToHashSet();
        var nextDisplayOrder = current.Count == 0 ? 0 : current.Max(link => link.DisplayOrder) + 1;

        foreach (var (targetId, desiredTarget) in desired)
        {
            if (currentTargets.Contains(targetId))
            {
                plan.AddOperation(
                    Area,
                    desiredTarget.Product.Name,
                    desiredTarget.Target.Display,
                    LinkedValue,
                    LinkedValue,
                    AkeneoDryRunOperationType.NoChange);
                continue;
            }

            // Akeneo associations have no order, so new links follow the existing ones.
            var displayOrder = nextDisplayOrder++;

            plan.AddOperation(
                Area,
                desiredTarget.Product.Name,
                desiredTarget.Target.Display,
                NotLinkedValue,
                LinkedValue,
                AkeneoDryRunOperationType.Add,
                executeAsync: async _ =>
                {
                    if (product == null)
                        return false;

                    var linkId = await InsertLinkAsync(product.Id, targetId, displayOrder);

                    if (work.SyncProfileId.HasValue)
                    {
                        await _managedRelationService.UpsertAsync(
                            work.SyncProfileId.Value,
                            work.SyncRunRecordId,
                            product.Id,
                            RelationType,
                            linkId,
                            akeneoValueCode: desiredTarget.Target.Reference);
                    }

                    return true;
                });
        }

        if (work.Mode == AkeneoCollectionSyncMode.Merge)
            return unresolved;

        if (unresolved > 0)
        {
            plan.Warnings.Add(
                $"{CapitalNoun} removal was skipped because some {Noun}s are not in nopCommerce yet.");
            plan.CoverageNotes.Add(
                $"{CapitalNoun} removals are not planned while any target is unresolved. The real synchronizer executes this same plan.");
            return unresolved;
        }

        if (work.Mode == AkeneoCollectionSyncMode.ReplaceAll)
        {
            foreach (var stale in current.Where(link => !desired.ContainsKey(link.TargetProductId)))
            {
                plan.AddOperation(
                    Area,
                    await TargetNameAsync(stale.TargetProductId),
                    null,
                    LinkedValue,
                    "Removed",
                    AkeneoDryRunOperationType.Remove,
                    $"Replace-all makes the {Noun}s match Akeneo.",
                    executeAsync: async _ =>
                    {
                        await DeleteLinkAsync(stale);
                        await _managedRelationService.DeleteByRelationEntityAsync(
                            RelationType,
                            stale.Id);
                        return true;
                    });
            }

            return unresolved;
        }

        if (!work.SyncProfileId.HasValue || product == null)
        {
            plan.Warnings.Add(
                $"Replace-managed {Noun} synchronization requires a saved sync profile. No {Noun}s were removed.");
            plan.CoverageNotes.Add(
                $"Replace-managed {Noun} removals require a saved sync profile and cannot be planned without one.");
            return unresolved;
        }

        var managedRelations = await _managedRelationService.GetByProductAsync(
            work.SyncProfileId.Value,
            product.Id,
            RelationType);

        foreach (var relation in managedRelations)
        {
            var link = current.FirstOrDefault(existing => existing.Id == relation.NopRelationEntityId);

            if (link == null)
            {
                // The link was removed outside the plugin; forget it.
                plan.AddInternalOperation(async _ =>
                {
                    await _managedRelationService.DeleteAsync(relation);
                    return false;
                });
                continue;
            }

            if (desired.ContainsKey(link.TargetProductId))
                continue;

            plan.AddOperation(
                Area,
                await TargetNameAsync(link.TargetProductId),
                relation.AkeneoValueCode,
                LinkedValue,
                "Removed",
                AkeneoDryRunOperationType.Remove,
                $"This {Noun} link is owned by the plugin and is stale for this profile.",
                executeAsync: async _ =>
                {
                    await DeleteLinkAsync(link);
                    await _managedRelationService.DeleteAsync(relation);
                    return true;
                });
        }

        return unresolved;
    }

    /// <summary>The nopCommerce product a target is shown as, or null when it is not there yet.</summary>
    internal async Task<int?> ResolveAsync(AkeneoRelatedProductTarget target, int? syncProfileId)
    {
        if (target.EntityType == AkeneoEntityType.ProductModel)
        {
            return await _entityMappingService.GetMappedNopEntityIdByAkeneoCodeAsync(
                AkeneoEntityType.ProductModel,
                target.Reference,
                NopEntityType.Product);
        }

        // Products are listed by identifier or by UUID depending on the endpoint.
        var productId =
            await _entityMappingService.GetMappedNopEntityIdByAkeneoCodeAsync(
                AkeneoEntityType.Product, target.Reference, NopEntityType.Product) ??
            await _entityMappingService.GetMappedNopEntityIdByAkeneoUuidAsync(
                AkeneoEntityType.Product, target.Reference, NopEntityType.Product);

        if (productId.HasValue)
            return productId;

        // A variant shown as an attribute combination: link its parent product.
        var combinationId =
            await _entityMappingService.GetMappedNopEntityIdByAkeneoCodeAsync(
                AkeneoEntityType.Product, target.Reference, NopEntityType.ProductAttributeCombination) ??
            await _entityMappingService.GetMappedNopEntityIdByAkeneoUuidAsync(
                AkeneoEntityType.Product, target.Reference, NopEntityType.ProductAttributeCombination);

        if (combinationId.HasValue && _productAttributeService != null)
        {
            var combination = await _productAttributeService.GetProductAttributeCombinationByIdAsync(combinationId.Value);
            if (combination != null)
                return combination.ProductId;
        }

        // A combination synced by this profile records its parent product in the sync state.
        if (syncProfileId is > 0)
        {
            var state = await _syncStateService.GetBySourceAsync(
                syncProfileId.Value,
                AkeneoEntityType.Product,
                target.Reference,
                target.Reference);

            if (state?.NopProductId is > 0)
                return state.NopProductId;
        }

        return null;
    }

    private async Task<string> TargetNameAsync(int productId) =>
        (await ProductService.GetProductByIdAsync(productId))?.Name ?? $"Product #{productId}";
}
