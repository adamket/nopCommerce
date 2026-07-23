using Nop.Core;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;

/// <summary>
/// Tracks the exact nopCommerce asset relationship owned by this plugin.
/// Manual pictures and videos are never claimed by this table.
/// </summary>
public class AkeneoManagedAsset : BaseEntity
{
    public int SyncProfileId { get; set; }
    public int AssetMappingId { get; set; }
    public string AssetMappingKey { get; set; }
    public int NopProductId { get; set; }
    public int DestinationTypeId { get; set; }

    public int? NopPictureId { get; set; }
    public int? NopProductPictureId { get; set; }
    public int? NopVideoId { get; set; }
    public int? NopProductVideoId { get; set; }

    public string SourceIdentityHash { get; set; }
    public string SourceFingerprint { get; set; }
    public string SourceAttributeCode { get; set; }
    public string AssetFamilyCode { get; set; }
    public string AssetCode { get; set; }
    public string MediaFileCode { get; set; }
    public string SourceUrl { get; set; }
    public string DestinationKey { get; set; }
    public int DisplayOrder { get; set; }
    public int LastSeenRunRecordId { get; set; }

    public DateTime CreatedOnUtc { get; set; }
    public DateTime UpdatedOnUtc { get; set; }
}
