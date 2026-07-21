using Microsoft.AspNetCore.Mvc.Rendering;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Models;

/// <summary>
/// Describes an Akeneo attribute independently of any saved nopCommerce mapping.
/// Reference-entity definitions use this model to render their outer container
/// even when no field mappings have been created.
/// </summary>
public record AkeneoAttributeDefinitionModel
{
    public string Code { get; set; }
    public string Label { get; set; }
    public string Type { get; set; }
    public string GroupCode { get; set; }
    public string GroupLabel { get; set; }

    public int AttributeTypeId { get; set; }
    public bool IsLocalizable { get; set; }
    public bool IsScopable { get; set; }

    public bool IsReferenceEntityAttribute { get; set; }
    public string ReferenceEntityCode { get; set; }

    public int DefaultNopTargetTypeId { get; set; }
    public string DefaultNopTargetKey { get; set; }

    public IList<SelectListItem> AvailableTargetTypes { get; set; }
        = new List<SelectListItem>();

    public IList<SelectListItem> AvailableSpecificationAttributes { get; set; }
        = new List<SelectListItem>();

    public IList<SelectListItem> AvailableProductAttributes { get; set; }
        = new List<SelectListItem>();

    public IList<SelectListItem> AvailableReferenceEntityAttributes { get; set; }
        = new List<SelectListItem>();
}
