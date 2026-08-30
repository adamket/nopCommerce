using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Web.Framework.Models;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Models;

public record AkeneoFamilyMappingModel : BaseNopEntityModel
{
    public string AkeneoFamilyCode { get; set; }

    public string AkeneoFamilyVariantCode { get; set; }

    public bool Enabled { get; set; } = true;

    public int VariantRelationshipModeId { get; set; }

    public int ProductModelHierarchyModeId { get; set; }

    public bool PreserveExistingNopVariantStructure { get; set; } = true;

    public int? AssociatedProductAttributeId { get; set; }

    public string AssociatedValueNameTemplate { get; set; } = "{axes}";

    public bool HideChildProductsWhenRepresentedByParent { get; set; } = true;

    public int DisplayOrder { get; set; }

    public IList<AkeneoFamilyVariantAxisMappingModel> AxisMappings { get; set; } =
        new List<AkeneoFamilyVariantAxisMappingModel>();


    public IList<AkeneoFamilySubModelRuleModel> SubModelRules { get; set; } = new List<AkeneoFamilySubModelRuleModel>();

    public IList<SelectListItem> AvailableVariantRelationshipModes { get; set; } =
        new List<SelectListItem>();

    public IList<SelectListItem> AvailableProductModelHierarchyModes { get; set; } =
        new List<SelectListItem>();

    public IList<SelectListItem> AvailableProductAttributes { get; set; } =
        new List<SelectListItem>();

    public IList<SelectListItem> AvailableAkeneoFamilies { get; set; } =
        new List<SelectListItem>();

    public IList<SelectListItem> AvailableAkeneoFamilyVariants { get; set; } =
        new List<SelectListItem>();

    public IList<SelectListItem> AvailableAkeneoAttributes { get; set; } =
        new List<SelectListItem>();

    public IList<SelectListItem> AvailableSubModelAxisAttributes { get; set; } =
        new List<SelectListItem>();

    public IList<SelectListItem> AvailableVariantAxisAttributes { get; set; } =
        new List<SelectListItem>();
}
