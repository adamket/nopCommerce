using System.Transactions;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Nop.Services.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public class AkeneoCatalogReconciliationService(
    IAkeneoProductSyncStateService syncStateService,
    IProductService productService,
    IProductAttributeService productAttributeService,
    IAkeneoManagedRelationService managedRelationService,
    IAkeneoManagedVariantCleanupService managedVariantCleanupService)
    : IAkeneoCatalogReconciliationService
{
    public async Task<int> ReconcileUnseenAsync(
        AkeneoProductBatchImportRequest request,
        AkeneoProductBatchImportResult result,
        CancellationToken cancellationToken = default)
    {
        if (!request.SyncProfileId.HasValue || request.SyncProfileId.Value <= 0)
            return 0;

        if (!result.IsAuthoritative)
            return 0;

        var unseen = await syncStateService.GetUnseenStatesAsync(
            request.SyncProfileId.Value,
            request.SyncRunRecordId);

        if (!unseen.Any() || request.MissingProductBehavior == AkeneoMissingProductBehavior.Ignore)
        {
            result.ReconciliationCompleted = true;
            return 0;
        }

        var changedCount = 0;

        foreach (var state in unseen)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var transaction = CreateUnitTransaction();

                var changed = await ApplyMissingBehaviorAsync(
                    state,
                    request.MissingProductBehavior);

                transaction.Complete();

                if (changed)
                    changedCount++;
            }
            catch (Exception ex)
            {
                result.AddError(
                    $"Could not reconcile missing Akeneo item '{state.AkeneoUuid ?? state.AkeneoCode}': {ex.Message}");
            }
        }

        result.ReconciliationCompleted = !result.Errors.Any();

        if (changedCount > 0)
        {
            result.AddMessage(
                $"Reconciled {changedCount} product representation(s) no longer present in the authoritative profile scope.");
        }

        return changedCount;
    }

    private async Task<bool> ApplyMissingBehaviorAsync(
        AkeneoProductSyncState state,
        AkeneoMissingProductBehavior behavior)
    {
        var destinationKind = (AkeneoProductDestinationKind)state.DestinationKindId;

        // A missing combination represents one removed variant, not a missing
        // parent product. Remove the combination for any active lifecycle policy.
        if (destinationKind == AkeneoProductDestinationKind.ProductAttributeCombination)
        {
            if (state.NopProductAttributeCombinationId.HasValue)
            {
                var combination = await productAttributeService
                    .GetProductAttributeCombinationByIdAsync(
                        state.NopProductAttributeCombinationId.Value);

                if (combination != null)
                {
                    await managedVariantCleanupService
                        .DeleteCombinationAndUnusedAxisValuesAsync(
                            state.SyncProfileId,
                            combination);
                }
                else
                {
                    var relation = await managedRelationService.GetByRelationEntityAsync(
                        state.SyncProfileId,
                        AkeneoManagedRelationType.ProductAttributeCombination,
                        state.NopProductAttributeCombinationId.Value);

                    await managedRelationService.DeleteAsync(relation);
                }
            }

            await syncStateService.UpdateLifecycleStatusAsync(
                state,
                AkeneoProductLifecycleStatus.MissingFromAuthoritativeScope);

            return true;
        }

        if (state.NopProductAttributeValueId.HasValue)
        {
            var value = await productAttributeService.GetProductAttributeValueByIdAsync(
                state.NopProductAttributeValueId.Value);

            if (value != null)
                await productAttributeService.DeleteProductAttributeValueAsync(value);

            var relation = await managedRelationService.GetByRelationEntityAsync(
                state.SyncProfileId,
                AkeneoManagedRelationType.AssociatedProductAttributeValue,
                state.NopProductAttributeValueId.Value);

            await managedRelationService.DeleteAsync(relation);
        }

        var product = await productService.GetProductByIdAsync(state.NopProductId);

        if (product == null)
        {
            await syncStateService.UpdateLifecycleStatusAsync(
                state,
                AkeneoProductLifecycleStatus.MissingFromAuthoritativeScope);
            return false;
        }

        switch (behavior)
        {
            case AkeneoMissingProductBehavior.Unpublish:
                if (product.Published)
                {
                    product.Published = false;
                    product.UpdatedOnUtc = DateTime.UtcNow;
                    await productService.UpdateProductAsync(product);
                }

                await syncStateService.UpdateLifecycleStatusAsync(
                    state,
                    AkeneoProductLifecycleStatus.Unpublished);
                return true;

            case AkeneoMissingProductBehavior.DisablePurchasing:
                if (!product.DisableBuyButton)
                {
                    product.DisableBuyButton = true;
                    product.UpdatedOnUtc = DateTime.UtcNow;
                    await productService.UpdateProductAsync(product);
                }

                await syncStateService.UpdateLifecycleStatusAsync(
                    state,
                    AkeneoProductLifecycleStatus.PurchasingDisabled);
                return true;

            case AkeneoMissingProductBehavior.DetachFromParent:
                if (product.ParentGroupedProductId != 0)
                {
                    product.ParentGroupedProductId = 0;
                    product.VisibleIndividually = true;
                    product.UpdatedOnUtc = DateTime.UtcNow;
                    await productService.UpdateProductAsync(product);
                }

                await syncStateService.UpdateLifecycleStatusAsync(
                    state,
                    AkeneoProductLifecycleStatus.Detached);
                return true;

            case AkeneoMissingProductBehavior.SoftDelete:
                if (!product.Deleted)
                {
                    product.Deleted = true;
                    product.Published = false;
                    product.UpdatedOnUtc = DateTime.UtcNow;
                    await productService.UpdateProductAsync(product);
                }

                await syncStateService.UpdateLifecycleStatusAsync(
                    state,
                    AkeneoProductLifecycleStatus.SoftDeleted);
                return true;

            case AkeneoMissingProductBehavior.Delete:
                await productService.DeleteProductAsync(product);
                await syncStateService.UpdateLifecycleStatusAsync(
                    state,
                    AkeneoProductLifecycleStatus.Deleted);
                return true;

            default:
                return false;
        }
    }

    private static TransactionScope CreateUnitTransaction() => new(
        TransactionScopeOption.Required,
        new TransactionOptions
        {
            IsolationLevel = IsolationLevel.ReadCommitted
        },
        TransactionScopeAsyncFlowOption.Enabled);
}
