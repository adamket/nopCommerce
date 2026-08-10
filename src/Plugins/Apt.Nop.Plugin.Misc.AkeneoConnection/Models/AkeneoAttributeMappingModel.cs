using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Web.Framework.Models;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Models;

public record AkeneoAttributeMappingModel : BaseNopEntityModel
{
    public string MappingKey { get; set; }
    public string Name { get; set; }
    public int ValueModeId { get; set; }
    public string ValueTemplate { get; set; }
    public bool IsComputed =>
        ValueModeId == (int)AkeneoAttributeMappingValueMode.Template;

    public bool IsInherited { get; set; }
    public string AkeneoFamilyCode { get; set; }
    public string AkeneoAttributeCode { get; set; }
    public string AkeneoAttributeLabel { get; set; }
    public string AkeneoAttributeType { get; set; }

    public bool IsLocalizable { get; set; }
    public bool IsScopable { get; set; }

    public int TargetTypeId { get; set; }
    public int? NopTargetEntityId { get; set; }
    public bool IsEnabled { get; set; }

    public int AkeneoAttributeTypeId { get; set; }
    public int NopTargetTypeId { get; set; }
    public string NopTargetKey { get; set; }
    public int SpecificationMissingValueHandlingId { get; set; } =
        (int)AkeneoSpecificationMissingValueHandling.CreateSpecificationAttributeOption;

    /// <summary>
    /// Default destination suggested by the Akeneo attribute metadata when no
    /// persisted mapping exists. Used by the AJAX delete flow to restore a
    /// standard attribute row without reloading the page.
    /// </summary>
    public int DefaultNopTargetTypeId { get; set; }
    public string DefaultNopTargetKey { get; set; }
    public string Locale { get; set; }
    public string Channel { get; set; }
    public string TransformRuleJson { get; set; }
    public bool IsRequired { get; set; }
    public int EntityScopeId { get; set; }
    public string FallbackSourcesJson { get; set; }
    public IList<AkeneoAttributeMappingFallbackSourceModel> FallbackSources { get; set; } = new List<AkeneoAttributeMappingFallbackSourceModel>();
    public string AkeneoAttributeGroup { get; set; }
    public string AkeneoAttributeGroupLabel { get; set; }

    public bool IsReferenceEntityAttribute { get; set; }
    public string AkeneoReferenceEntityCode { get; set; }
    public string AkeneoReferenceEntityAttributeCode { get; set; }

    public IList<SelectListItem> AvailableTargetTypes { get; set; } = new List<SelectListItem>();
    public IList<SelectListItem> AvailableSpecificationAttributes { get; set; } = new List<SelectListItem>();
    public IList<SelectListItem> AvailableProductAttributes { get; set; } = new List<SelectListItem>();
    public IList<SelectListItem> AvailableNopTargetKeys { get; set; } = new List<SelectListItem>();
    public IList<SelectListItem> AvailableReferenceEntityAttributes { get; set; } = new List<SelectListItem>();
}
