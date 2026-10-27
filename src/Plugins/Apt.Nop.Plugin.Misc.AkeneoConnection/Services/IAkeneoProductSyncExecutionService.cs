using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public interface IAkeneoProductSyncExecutionService
{
    Task<AkeneoProductSyncExecutionResult> ImportProductsByProfileAsync(
        int profileId,
        SyncType syncType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads every product in the profile's scope (optionally narrowed to some
    /// of its families) and writes the ones whose output changed, or all of
    /// them with Repair. Never disables or deletes products and never moves
    /// the delta watermark.
    /// </summary>
    Task<AkeneoProductSyncExecutionResult> ResyncProfileAsync(
        int profileId,
        AkeneoResyncOptions options,
        CancellationToken cancellationToken = default);

    Task<AkeneoProductSyncExecutionResult> SyncProductByUuidAsync(
        int profileId,
        string akeneoProductUuid,
        CancellationToken cancellationToken = default);

    Task<AkeneoProductSyncExecutionResult> SyncProductModelByCodeAsync(
        int profileId,
        string akeneoProductModelCode,
        CancellationToken cancellationToken = default);
}