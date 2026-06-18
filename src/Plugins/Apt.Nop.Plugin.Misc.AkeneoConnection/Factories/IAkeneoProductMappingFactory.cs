using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Factories;
public interface IAkeneoProductMappingFactory
{
    Task<AkeneoProductMappingPreviewModel> PreviewProductMappingAsync(
        string akeneoIdentifier,
        string locale,
        string channel,
        string currency,
        CancellationToken cancellationToken = default);
}
