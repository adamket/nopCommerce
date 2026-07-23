using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Web.Framework.Models;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Models;

public record AkeneoAssetMappingModel : BaseNopEntityModel
{
    public string MappingKey { get; set; }
    public string Name { get; set; }
    public bool Enabled { get; set; } = true;
    public string AkeneoFamilyCode { get; set; }
    public bool IsInherited { get; set; }

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
    public int MaxAssets { get; set; } = 20;
}

public record AkeneoAssetMappingListModel : BaseNopModel
{
    public string AkeneoFamilyCode { get; set; }
    public IList<SelectListItem> AvailableAkeneoFamilies { get; set; } = new List<SelectListItem>();
    public IList<AkeneoAssetSourceOptionModel> AvailableSources { get; set; } = new List<AkeneoAssetSourceOptionModel>();
    public IList<AkeneoAssetMappingModel> Mappings { get; set; } = new List<AkeneoAssetMappingModel>();
    public IList<string> Warnings { get; set; } = new List<string>();
}

public sealed class AkeneoAssetSourceOptionModel
{
    public string Code { get; set; }
    public string Label { get; set; }
    public string AttributeType { get; set; }
    public int SourceTypeId { get; set; }
    public string AssetFamilyCode { get; set; }
    public bool Localizable { get; set; }
    public bool Scopable { get; set; }
}

public sealed class AkeneoAssetAttributeOptionModel
{
    public string Code { get; set; }
    public string Label { get; set; }
    public string Type { get; set; }
    public string MediaType { get; set; }
    public bool IsMedia { get; set; }
}
