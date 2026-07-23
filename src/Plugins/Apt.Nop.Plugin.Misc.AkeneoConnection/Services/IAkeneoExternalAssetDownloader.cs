using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public interface IAkeneoExternalAssetDownloader
{
    Task<AkeneoBinaryFile> DownloadAsync(
        string url,
        CancellationToken cancellationToken = default);
}
