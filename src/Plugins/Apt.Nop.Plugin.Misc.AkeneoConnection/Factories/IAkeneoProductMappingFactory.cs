using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Factories;

public interface IAkeneoProductMappingFactory
{
    /// <summary>
    /// Previews one product in the profile's own value context (locale,
    /// channel, currency), exactly as a one-time import would read it.
    /// </summary>
    Task<AkeneoProductMappingPreviewModel> PreviewProductMappingAsync(
        string akeneoIdentifier,
        AkeneoSyncProfile profile,
        CancellationToken cancellationToken = default);
}
