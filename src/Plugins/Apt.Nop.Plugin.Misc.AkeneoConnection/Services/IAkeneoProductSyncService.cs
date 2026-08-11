using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Nop.Core.Domain.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
public interface IAkeneoProductSyncService
{

    /// <summary>
    /// Resolves the source-side synchronization intent without binding to the
    /// current nopCommerce destination state. The returned intent may be reused
    /// within a reconciliation unit.
    /// </summary>
    Task<AkeneoResolvedProductIntent> ResolveIntentAsync(
        AkeneoProductDefinition source,
        AkeneoEntityType sourceEntityType,
        AkeneoProductImportRequest request,
        CancellationToken cancellationToken = default);

    Task<AkeneoResolvedProductIntent> ResolveIntentAsync(
        AkeneoProductDefinition source,
        AkeneoEntityType sourceEntityType,
        AkeneoProductImportRequest request,
        string mappingFamilyCode,
        CancellationToken cancellationToken = default);

    Task<AkeneoResolvedProductIntent> ResolveIntentAsync(
        AkeneoProductDefinition source,
        AkeneoEntityType sourceEntityType,
        AkeneoProductImportRequest request,
        string mappingFamilyCode,
        AkeneoAttributeMappingEntityScope mappingEntityScope,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a fresh destination-bound synchronization context from a
    /// previously resolved source intent. Destination state is deliberately
    /// re-read so intent reuse cannot make the write path stale.
    /// </summary>
    Task<AkeneoProductSyncContext> PrepareFromIntentAsync(
        AkeneoResolvedProductIntent intent,
        AkeneoProductImportResult result,
        CancellationToken cancellationToken = default);
    Task<AkeneoProductSyncContext> PrepareAsync(
        AkeneoProductDefinition source,
        AkeneoEntityType sourceEntityType,
        AkeneoProductImportRequest request,
        AkeneoProductImportResult result,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Prepares a synchronization context using an explicit family for
    /// family-scoped attribute mappings. Akeneo product-model payloads do not
    /// expose the family code, so their family must be supplied by the leaf
    /// product that led to the model synchronization.
    /// </summary>
    Task<AkeneoProductSyncContext> PrepareAsync(
        AkeneoProductDefinition source,
        AkeneoEntityType sourceEntityType,
        AkeneoProductImportRequest request,
        AkeneoProductImportResult result,
        string mappingFamilyCode,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Prepares a synchronization context for an explicit nopCommerce product
    /// role. Use this when the source hierarchy alone does not describe the
    /// destination role, such as a variant imported as a standalone product.
    /// </summary>
    Task<AkeneoProductSyncContext> PrepareAsync(
        AkeneoProductDefinition source,
        AkeneoEntityType sourceEntityType,
        AkeneoProductImportRequest request,
        AkeneoProductImportResult result,
        string mappingFamilyCode,
        AkeneoAttributeMappingEntityScope mappingEntityScope,
        CancellationToken cancellationToken = default);

    Task<Product> SynchronizeAsync(
        AkeneoProductSyncContext context,
        CancellationToken cancellationToken = default);

    Task RunPrepareAsync(AkeneoProductSyncContext context, CancellationToken cancellationToken = default);
}
