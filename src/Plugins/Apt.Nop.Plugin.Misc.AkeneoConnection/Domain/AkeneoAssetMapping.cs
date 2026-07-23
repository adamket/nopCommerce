using Nop.Core;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;

/// <summary>
/// Describes how an Akeneo media attribute or Asset Manager collection is
/// materialized for a nopCommerce product.
/// </summary>
public class AkeneoAssetMapping : BaseEntity
{
    public string MappingKey { get; set; }
    public string Name { get; set; }
    public bool Enabled { get; set; }
    public string AkeneoFamilyCode { get; set; }

    public int SourceTypeId { get; set; }
    public string SourceAttributeCode { get; set; }
    public string AssetFamilyCode { get; set; }
    public string AssetMediaAttributeCode { get; set; }
    public string AssetMediaType { get; set; }

    public int DestinationTypeId { get; set; }
    public int StorageModeId { get; set; }
    public int EntityScopeId { get; set; }

    public string RoleAttributeCode { get; set; }
    public string RoleValuesCsv { get; set; }
    public string SortOrderAttributeCode { get; set; }

    public string AltTextTemplate { get; set; }
    public string TitleTextTemplate { get; set; }
    public string SeoFilenameTemplate { get; set; }
    public string CustomPropertyKey { get; set; }

    public int DisplayOrder { get; set; }
    public int MaxAssets { get; set; }

    public DateTime CreatedOnUtc { get; set; }
    public DateTime UpdatedOnUtc { get; set; }
}

public enum AkeneoAssetSourceType
{
    ProductMediaAttribute = 10,
    AssetCollection = 20
}

public enum AkeneoAssetDestinationType
{
    ProductPicture = 10,
    ProductVideo = 20,
    CustomProperty = 30
}

public enum AkeneoAssetStorageMode
{
    ImportIntoNopCommerce = 10,
    ExternalReference = 20
}
