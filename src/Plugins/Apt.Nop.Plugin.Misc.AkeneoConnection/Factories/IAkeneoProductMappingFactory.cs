using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Factories;

public interface IAkeneoProductMappingFactory
{
    Task<AkeneoProductMappingPreviewModel> PreviewProductMappingAsync(
        string akeneoIdentifier,
        string locale,
        string channel,
        string currency,
        AkeneoSyncProfile profile,
        CancellationToken cancellationToken = default);
}
