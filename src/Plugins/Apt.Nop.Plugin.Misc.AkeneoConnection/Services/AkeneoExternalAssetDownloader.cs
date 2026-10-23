using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
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

        await ResolvePublicAddressesAsync(uri.DnsSafeHost, cancellationToken);
    }

    /// <summary>
    /// The external-asset HttpClient's connect callback. It resolves the host
    /// itself and connects only to addresses it has just vetted, so a DNS
    /// answer that changes after <see cref="ValidatePublicHostAsync"/> cannot
    /// redirect the download to a private address.
    /// </summary>
    internal static async ValueTask<Stream> ConnectToPublicHostAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        var addresses = await ResolvePublicAddressesAsync(
            context.DnsEndPoint.Host,
            cancellationToken);
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };

        try
        {
            await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static async Task<IPAddress[]> ResolvePublicAddressesAsync(
        string host,
        CancellationToken cancellationToken)
    {
        var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
        if (addresses.Length == 0 || addresses.Any(IsPrivateOrLocal))
            throw new InvalidOperationException("Private or local asset hosts are not allowed.");

        return addresses;
    }

    internal static bool IsPrivateOrLocal(IPAddress address)
    {
        // An IPv4 address written in IPv6 form must pass the IPv4 rules.
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var v6 = address.GetAddressBytes();

            // NAT64 (64:ff9b::/96) embeds an IPv4 address in its last four bytes.
            if (v6[0] == 0x00 && v6[1] == 0x64 && v6[2] == 0xff && v6[3] == 0x9b &&
                v6.Skip(4).Take(8).All(part => part == 0))
            {
                return IsPrivateOrLocal(new IPAddress(v6[12..]));
            }

            return IPAddress.IsLoopback(address) ||
                   address.IsIPv6LinkLocal ||
                   address.IsIPv6SiteLocal ||
                   address.IsIPv6UniqueLocal ||
                   address.IsIPv6Multicast ||
                   address.Equals(IPAddress.IPv6Any) ||
                   address.Equals(IPAddress.IPv6None);
        }

        var bytes = address.GetAddressBytes();
        return bytes[0] == 10 ||
               bytes[0] == 127 ||
               (bytes[0] == 100 && bytes[1] is >= 64 and <= 127) ||
               (bytes[0] == 169 && bytes[1] == 254) ||
               (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
               (bytes[0] == 192 && bytes[1] == 168) ||
               (bytes[0] == 0) ||
               (bytes[0] >= 224);
    }
}
