using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Assets;

public sealed class AkeneoResolvedAsset
{
    public AkeneoAssetMapping Mapping { get; init; }

    public string SourceIdentityHash { get; init; }
    public string SourceFingerprint { get; init; }
    public string SourceAttributeCode { get; init; }

    public string AssetFamilyCode { get; init; }
    public string AssetCode { get; init; }

    public string MediaFileCode { get; init; }

    // Add this.
    public string DownloadUrl { get; init; }

    public string ExternalUrl { get; init; }
    public string MimeType { get; init; }
    public string OriginalFileName { get; init; }

    public string AltText { get; init; }
    public string TitleText { get; init; }
    public string SeoFilename { get; init; }

    public int DisplayOrder { get; init; }
    public DateTime? SourceUpdatedOnUtc { get; init; }
}