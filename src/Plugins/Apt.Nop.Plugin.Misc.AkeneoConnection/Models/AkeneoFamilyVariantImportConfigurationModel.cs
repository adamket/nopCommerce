using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Web.Framework.Models;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Models;

public record AkeneoFamilyVariantImportConfigurationModel : BaseNopEntityModel
{
    public string AkeneoFamilyCode { get; set; }

    public bool Enabled { get; set; } = true;

    public int VariantRelationshipModeId { get; set; }

    public bool PreserveExistingNopVariantStructure { get; set; } = true;

    public int? AssociatedProductAttributeId { get; set; }

    public string AssociatedValueNameTemplate { get; set; } = "{axes}";

    public bool HideChildProductsWhenRepresentedByParent { get; set; } = true;

    public int DisplayOrder { get; set; }

    public IList<AkeneoFamilyVariantAxisMappingModel> AxisMappings { get; set; } =
        new List<AkeneoFamilyVariantAxisMappingModel>();

    public IList<SelectListItem> AvailableVariantRelationshipModes { get; set; } =
        new List<SelectListItem>();

    public IList<SelectListItem> AvailableProductAttributes { get; set; } =
        new List<SelectListItem>();

    public IList<SelectListItem> AvailableAkeneoFamilies { get; set; } =
        new List<SelectListItem>();

    public IList<SelectListItem> AvailableAkeneoAttributes { get; set; } =
        new List<SelectListItem>();
}