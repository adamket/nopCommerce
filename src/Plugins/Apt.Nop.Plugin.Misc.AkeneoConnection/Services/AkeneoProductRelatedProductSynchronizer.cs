using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services.Planning;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
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

/// <summary>Everything needed to (re)apply one product's related products outside its sync context.</summary>
public sealed record AkeneoRelatedProductWork(
    int ProductId,
    string SourceCode,
    IReadOnlyList<AkeneoRelatedProductTarget> Targets,
    AkeneoCollectionSyncMode Mode,
    int? SyncProfileId,
    int SyncRunRecordId);

public sealed record AkeneoRelatedProductRetryOutcome(int Added, int Removed, int StillUnresolved);

/// <summary>
/// Syncs nopCommerce related products from one Akeneo association type (the profile's
/// <c>RelatedProductAssociationType</c>). Only the products a product model or a standalone
/// product becomes carry them; variant leaves are skipped, because Akeneo already reports a
/// model's associations on its variants. Targets are resolved through the plugin's bindings: a
/// product model to its nopCommerce product, a product to its own nopCommerce product, or to
/// the parent product when it is represented as an attribute combination. A target that is not
/// in nopCommerce yet is reported and, during a batch run, retried once every item has been
/// processed (<see cref="RetryAsync"/>). The profile's mode works like the category mode, and no
/// link is removed while any target is unresolved.
/// </summary>
public partial class AkeneoProductRelatedProductSynchronizer
    : IAkeneoProductSectionSynchronizer,
      IAkeneoSectionDryRunPlanProvider
{
    private const string Area = "Related products";

    private readonly IProductService _productService;
    private readonly IAkeneoNopEntityMappingService _entityMappingService;
    private readonly IAkeneoProductSyncStateService _syncStateService;
    private readonly IAkeneoManagedRelationService _managedRelationService;
    private readonly IProductAttributeService _productAttributeService;

    // Products from this sync scope with targets that were not in nopCommerce yet.
    private readonly List<AkeneoRelatedProductWork> _pending = new();

    public AkeneoProductRelatedProductSynchronizer(
        IProductService productService,
        IAkeneoNopEntityMappingService entityMappingService,
        IAkeneoProductSyncStateService syncStateService,
        IAkeneoManagedRelationService managedRelationService,
        IProductAttributeService productAttributeService = null)
    {
        _productService = productService;
        _entityMappingService = entityMappingService;
        _syncStateService = syncStateService;
        _managedRelationService = managedRelationService;
        _productAttributeService = productAttributeService;
    }

    public int Order => 700;

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
    /// Re-applies one product's related products once the rest of the run has been processed,
    /// so targets synced after their source are linked in the same run.
    /// </summary>
    public async Task<AkeneoRelatedProductRetryOutcome> RetryAsync(
        AkeneoRelatedProductWork work,
        CancellationToken cancellationToken = default)
    {
        var product = await _productService.GetProductByIdAsync(work.ProductId);
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

        if (request.RelatedProductSyncMode == AkeneoCollectionSyncMode.Disabled)
        {
            plan.CoverageNotes.Add(
                "Related product synchronization is disabled by the saved sync profile.");
            return (plan, null, 0);
        }

        // A variant inherits its model's associations; the model's product carries them.
        if (context.SourceEntityType == AkeneoEntityType.Product &&
            !string.IsNullOrWhiteSpace(context.Source?.Parent))
        {
            return (plan, null, 0);
        }

        var associationType = request.RelatedProductAssociationType?.Trim();

        if (string.IsNullOrEmpty(associationType))
        {
            plan.Warnings.Add(
                "Related product synchronization is on, but the sync profile has no association type. Related products were left unchanged.");
            return (plan, null, 0);
        }

        // Akeneo reports every association type on every product, so a missing one means the
        // code is wrong; it must never read as "no related products" and remove them all.
        if (context.Source?.Associations == null ||
            !context.Source.Associations.TryGetValue(associationType, out var association))
        {
            plan.AddReview(
                Area,
                associationType,
                associationType,
                $"Akeneo returned no '{associationType}' association for {context.SourceCode}. Check the association type on the sync profile; related products were left unchanged.");
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
            request.RelatedProductSyncMode,
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
                    $"Related {target.Display} is not in nopCommerce yet (not synced or bound), so it was not linked.");
                continue;
            }

            if (product != null && nopProductId.Value == product.Id)
                continue;

            var targetProduct = await _productService.GetProductByIdAsync(nopProductId.Value);

            if (targetProduct == null || targetProduct.Deleted)
            {
                unresolved++;
                plan.AddReview(
                    Area,
                    target.Reference,
                    target.Display,
                    $"Related {target.Display} maps to missing or deleted nopCommerce product ID {nopProductId.Value}, so it was not linked.");
                continue;
            }

            desired.TryAdd(targetProduct.Id, (target, targetProduct));
        }

        var current = product == null
            ? new List<RelatedProduct>()
            : (await _productService.GetRelatedProductsByProductId1Async(product.Id, showHidden: true)).ToList();

        var currentTargets = current.Select(related => related.ProductId2).ToHashSet();
        var nextDisplayOrder = current.Count == 0 ? 0 : current.Max(related => related.DisplayOrder) + 1;

        foreach (var (targetId, desiredTarget) in desired)
        {
            if (currentTargets.Contains(targetId))
            {
                plan.AddOperation(
                    Area,
                    desiredTarget.Product.Name,
                    desiredTarget.Target.Display,
                    "Related",
                    "Related",
                    AkeneoDryRunOperationType.NoChange);
                continue;
            }

            // Akeneo associations have no order, so new links follow the existing ones.
            var displayOrder = nextDisplayOrder++;

            plan.AddOperation(
                Area,
                desiredTarget.Product.Name,
                desiredTarget.Target.Display,
                "Not related",
                "Related",
                AkeneoDryRunOperationType.Add,
                executeAsync: async _ =>
                {
                    if (product == null)
                        return false;

                    var related = new RelatedProduct
                    {
                        ProductId1 = product.Id,
                        ProductId2 = targetId,
                        DisplayOrder = displayOrder
                    };

                    await _productService.InsertRelatedProductAsync(related);

                    if (work.SyncProfileId.HasValue)
                    {
                        await _managedRelationService.UpsertAsync(
                            work.SyncProfileId.Value,
                            work.SyncRunRecordId,
                            product.Id,
                            AkeneoManagedRelationType.RelatedProduct,
                            related.Id,
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
                "Related product removal was skipped because some related products are not in nopCommerce yet.");
            plan.CoverageNotes.Add(
                "Related product removals are not planned while any target is unresolved. The real synchronizer executes this same plan.");
            return unresolved;
        }

        if (work.Mode == AkeneoCollectionSyncMode.ReplaceAll)
        {
            foreach (var stale in current.Where(related => !desired.ContainsKey(related.ProductId2)))
            {
                plan.AddOperation(
                    Area,
                    await TargetNameAsync(stale.ProductId2),
                    null,
                    "Related",
                    "Removed",
                    AkeneoDryRunOperationType.Remove,
                    "Replace-all makes the related products match Akeneo.",
                    executeAsync: async _ =>
                    {
                        await _productService.DeleteRelatedProductAsync(stale);
                        await _managedRelationService.DeleteByRelationEntityAsync(
                            AkeneoManagedRelationType.RelatedProduct,
                            stale.Id);
                        return true;
                    });
            }

            return unresolved;
        }

        if (!work.SyncProfileId.HasValue || product == null)
        {
            plan.Warnings.Add(
                "Replace-managed related product synchronization requires a saved sync profile. No related products were removed.");
            plan.CoverageNotes.Add(
                "Replace-managed related product removals require a saved sync profile and cannot be planned without one.");
            return unresolved;
        }

        var managedRelations = await _managedRelationService.GetByProductAsync(
            work.SyncProfileId.Value,
            product.Id,
            AkeneoManagedRelationType.RelatedProduct);

        foreach (var relation in managedRelations)
        {
            var link = current.FirstOrDefault(related => related.Id == relation.NopRelationEntityId);

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

            if (desired.ContainsKey(link.ProductId2))
                continue;

            plan.AddOperation(
                Area,
                await TargetNameAsync(link.ProductId2),
                relation.AkeneoValueCode,
                "Related",
                "Removed",
                AkeneoDryRunOperationType.Remove,
                "This related product link is owned by the plugin and is stale for this profile.",
                executeAsync: async _ =>
                {
                    await _productService.DeleteRelatedProductAsync(link);
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

        // A variant shown as an attribute combination: relate its parent product.
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
        (await _productService.GetProductByIdAsync(productId))?.Name ?? $"Product #{productId}";
}
