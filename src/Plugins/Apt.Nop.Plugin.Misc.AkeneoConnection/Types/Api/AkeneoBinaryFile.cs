namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api;

public sealed class AkeneoBinaryFile
{
    public byte[] Bytes { get; init; } = Array.Empty<byte>();
    public string ContentType { get; init; }
    public string FileName { get; init; }
    public string ETag { get; init; }
    public DateTimeOffset? LastModified { get; init; }
}
