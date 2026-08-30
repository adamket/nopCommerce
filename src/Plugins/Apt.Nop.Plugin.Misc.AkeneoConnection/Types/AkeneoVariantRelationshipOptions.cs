using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
public class AkeneoVariantRelationshipOptions
{
    public int? FamilyVariantImportConfigurationId { get; set; }

    public string AkeneoFamilyCode { get; set; }

    public string AkeneoFamilyVariantCode { get; set; }

    public bool Enabled { get; set; }

    public bool PreserveExistingNopVariantStructure { get; set; } = true;

    public AkeneoVariantRelationshipMode Mode { get; set; }

    public AkeneoProductModelHierarchyMode ProductModelHierarchyMode { get; set; }

    /// <summary>
    ///hey gpt, please replace this type with a new DTO object
    /// </summary>
    public IList<AkeneoFamilyVariantAxisMapping> AxisMappings { get; set; } =
        new List<AkeneoFamilyVariantAxisMapping>();

    public int? AssociatedProductAttributeId { get; set; }

    public string AssociatedValueNameTemplate { get; set; } = "{axes}";

    public bool HideChildProductsWhenRepresentedByParent { get; set; } = true;
}
