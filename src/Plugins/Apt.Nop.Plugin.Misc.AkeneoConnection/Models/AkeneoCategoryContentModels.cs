using Nop.Web.Framework.Models;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Models;

/// <summary>One category content mapping, as edited on the Category Mapping page.</summary>
public record AkeneoCategoryContentMappingModel : BaseNopModel
{
    public int Id { get; set; }

    public int TargetId { get; set; }

    public int LanguageId { get; set; }

    public int SourceTypeId { get; set; }

    public string AkeneoAttributeCode { get; set; }

    public string Locale { get; set; }

    public string Channel { get; set; }

    public int EmptyValueHandlingId { get; set; }
}

/// <summary>
/// A category template attribute found on the Akeneo categories, with the
/// locale/channel combinations its values use.
/// </summary>
public record AkeneoCategoryTemplateAttributeModel
{
    public string Code { get; set; }

    public string Type { get; set; }

    public bool IsImage { get; set; }

    public int CategoryCount { get; set; }

    public IList<AkeneoCategoryValueScopeModel> Scopes { get; set; } = new List<AkeneoCategoryValueScopeModel>();
}

public record AkeneoCategoryValueScopeModel
{
    public string Locale { get; set; }

    public string Channel { get; set; }
}
