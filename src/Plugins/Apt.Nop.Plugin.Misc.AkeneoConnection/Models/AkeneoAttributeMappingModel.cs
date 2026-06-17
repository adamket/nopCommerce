
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
public class AkeneoAttributeMappingModel
{
    public int Id { get; set; }

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
    public string Locale { get; set; }
    public string Channel { get; set; }
    public string TransformRuleJson { get; set; }
    public bool IsRequired { get; set; }
    public string AkeneoAttributeGroup { get; set; }

    public string AkeneoAttributeGroupLabel { get; set; }

    public IList<SelectListItem> AvailableTargetTypes { get; set; } = new List<SelectListItem>();
    public IList<SelectListItem> AvailableSpecificationAttributes { get; set; } = new List<SelectListItem>();
    public IList<SelectListItem> AvailableProductAttributes { get; set; } = new List<SelectListItem>();
    public IList<SelectListItem> AvailableNopTargetKeys { get; set; } = new List<SelectListItem>();
}