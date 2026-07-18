using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Nop.Services.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

/// <summary>
/// Removes only the obsolete representation explicitly bound to the source
/// leaf in AkeneoProductSyncState. General collection cleanup remains with the
/// relevant destination synchronizer.
/// </summary>
public class AkeneoVariantRepresentationCleanupService(
    IProductService productService,
    IProductAttributeService productAttributeService,
    IAkeneoManagedRelationService managedRelationService,
    IAkeneoManagedVariantCleanupService managedVariantCleanupService)
    : IAkeneoVariantRepresentationCleanupService
{
    public async Task CleanupPreviousRepresentationAsync(
        AkeneoProductSyncState previousState,
        AkeneoProductImportResult desiredResult,
        CancellationToken cancellationToken = default)
    {
        if (previousState == null || desiredResult == null)
            return;

        cancellationToken.ThrowIfCancellationRequested();

        var previousKind =
            (AkeneoProductDestinationKind)previousState.DestinationKindId;

        var sameBinding =
            previousKind == desiredResult.DestinationKind &&
            previousState.NopProductId == desiredResult.NopProductId &&
            previousState.NopProductAttributeCombinationId ==
                desiredResult.NopProductAttributeCombinationId &&
            previousState.NopProductAttributeValueId ==
                desiredResult.NopProductAttributeValueId;

        if (sameBinding)
            return;

        if (previousState.NopProductAttributeCombinationId.HasValue &&
            previousState.NopProductAttributeCombinationId !=
                desiredResult.NopProductAttributeCombinationId)
        {
            var combination = await productAttributeService
                .GetProductAttributeCombinationByIdAsync(
                    previousState.NopProductAttributeCombinationId.Value);

            if (combination != null)
            {
                await managedVariantCleanupService
                    .DeleteCombinationAndUnusedAxisValuesAsync(
                        previousState.SyncProfileId,
                        combination,
                        cancellationToken);
            }
            else
            {
                await DeleteManagedRelationAsync(
                    previousState,
                    AkeneoManagedRelationType.ProductAttributeCombination,
                    previousState.NopProductAttributeCombinationId.Value);
            }
        }

        if (previousState.NopProductAttributeValueId.HasValue &&
            previousState.NopProductAttributeValueId !=
                desiredResult.NopProductAttributeValueId)
        {
            var value = await productAttributeService
                .GetProductAttributeValueByIdAsync(
                    previousState.NopProductAttributeValueId.Value);

            if (value != null)
                await productAttributeService.DeleteProductAttributeValueAsync(value);

            await DeleteManagedRelationAsync(
                previousState,
                AkeneoManagedRelationType.AssociatedProductAttributeValue,
                previousState.NopProductAttributeValueId.Value);
        }

        if (previousKind == AkeneoProductDestinationKind.GroupedChildProduct &&
            desiredResult.DestinationKind !=
                AkeneoProductDestinationKind.GroupedChildProduct)
        {
            var oldChild = await productService
                .GetProductByIdAsync(previousState.NopProductId);

            if (oldChild != null && oldChild.ParentGroupedProductId != 0)
            {
                oldChild.ParentGroupedProductId = 0;
                oldChild.UpdatedOnUtc = DateTime.UtcNow;
                await productService.UpdateProductAsync(oldChild);
            }

            await DeleteManagedRelationAsync(
                previousState,
                AkeneoManagedRelationType.GroupedProductRelationship,
                previousState.NopProductId);
        }

        if ((previousKind is AkeneoProductDestinationKind.GroupedChildProduct or
             AkeneoProductDestinationKind.AssociatedProduct) &&
            desiredResult.DestinationKind ==
                AkeneoProductDestinationKind.ProductAttributeCombination)
        {
            var oldChild = await productService
                .GetProductByIdAsync(previousState.NopProductId);

            if (oldChild != null)
            {
                oldChild.ParentGroupedProductId = 0;
                oldChild.VisibleIndividually = false;
                oldChild.DisableBuyButton = true;
                oldChild.UpdatedOnUtc = DateTime.UtcNow;
                await productService.UpdateProductAsync(oldChild);
            }
        }
    }

    private async Task DeleteManagedRelationAsync(
        AkeneoProductSyncState state,
        AkeneoManagedRelationType relationType,
        int relationEntityId)
    {
        var relation = await managedRelationService.GetByRelationEntityAsync(
            state.SyncProfileId,
            relationType,
            relationEntityId);

        await managedRelationService.DeleteAsync(relation);
    }
}
