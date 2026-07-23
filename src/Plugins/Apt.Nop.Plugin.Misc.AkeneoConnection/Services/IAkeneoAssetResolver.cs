using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Assets;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public interface IAkeneoAssetResolver
{
    Task<AkeneoAssetResolutionResult> ResolveAsync(
        AkeneoProductSyncContext context,
        AkeneoAssetMapping mapping,
        CancellationToken cancellationToken = default);
}
