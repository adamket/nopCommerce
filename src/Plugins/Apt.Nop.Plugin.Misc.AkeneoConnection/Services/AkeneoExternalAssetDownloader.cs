using System.Net;
using System.Net.Http.Headers;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

/// <summary>
/// Downloads explicitly configured external DAM assets while blocking local,
/// private, and link-local destinations.
/// </summary>
public sealed class AkeneoExternalAssetDownloader(
    IHttpClientFactory httpClientFactory)
    : IAkeneoExternalAssetDownloader
{
    private const int MaximumAssetBytes = 50 * 1024 * 1024;

    public async Task<AkeneoBinaryFile> DownloadAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidOperationException(
                "External asset URL must be an absolute HTTP or HTTPS URL.");
        }

        await ValidatePublicHostAsync(uri, cancellationToken);

        var client = httpClientFactory.CreateClient(
            AkeneoConnectionConstants.ExternalAssetHttpClientName);

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));

        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if ((int)response.StatusCode is >= 300 and < 400)
        {
            throw new InvalidOperationException(
                "External asset redirects are not followed. Configure the final public asset URL.");
        }

        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength > MaximumAssetBytes)
            throw new InvalidOperationException("External asset exceeds the 50 MB download limit.");

        var bytes = await ReadBoundedAsync(
            response.Content,
            MaximumAssetBytes,
            cancellationToken);

        var fileName = response.Content.Headers.ContentDisposition?.FileNameStar ??
            response.Content.Headers.ContentDisposition?.FileName ??
            Path.GetFileName(uri.LocalPath);

        return new AkeneoBinaryFile
        {
            Bytes = bytes,
            ContentType = response.Content.Headers.ContentType?.MediaType,
            FileName = fileName?.Trim('"'),
            ETag = response.Headers.ETag?.Tag,
            LastModified = response.Content.Headers.LastModified
        };
    }

    private static async Task<byte[]> ReadBoundedAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        await using var input = await content.ReadAsStreamAsync(cancellationToken);
        await using var output = new MemoryStream();
        var buffer = new byte[81920];
        var total = 0;

        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0)
                break;

            total += read;
            if (total > maximumBytes)
                throw new InvalidOperationException("External asset exceeds the 50 MB download limit.");

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return output.ToArray();
    }

    private static async Task ValidatePublicHostAsync(
        Uri uri,
        CancellationToken cancellationToken)
    {
        if (uri.IsLoopback || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Local asset URLs are not allowed.");

        var addresses = await Dns.GetHostAddressesAsync(uri.DnsSafeHost, cancellationToken);
        if (addresses.Length == 0 || addresses.Any(IsPrivateOrLocal))
            throw new InvalidOperationException("Private or local asset hosts are not allowed.");
    }

    private static bool IsPrivateOrLocal(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.IsIPv6LinkLocal ||
            address.IsIPv6SiteLocal || address.IsIPv6Multicast)
        {
            return true;
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            return address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.IPv6None);

        var bytes = address.GetAddressBytes();
        return bytes[0] == 10 ||
               bytes[0] == 127 ||
               (bytes[0] == 169 && bytes[1] == 254) ||
               (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
               (bytes[0] == 192 && bytes[1] == 168) ||
               (bytes[0] == 0) ||
               (bytes[0] >= 224);
    }
}
