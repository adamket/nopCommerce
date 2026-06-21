using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
public class AkeneoVariantRelationshipOptions
{
    public int? FamilyVariantImportConfigurationId { get; set; }

    public string AkeneoFamilyCode { get; set; }

    public bool Enabled { get; set; }

    public bool PreserveExistingNopVariantStructure { get; set; } = true;

    public AkeneoVariantRelationshipMode Mode { get; set; }

    public IList<AkeneoVariantAxisMapping> AxisMappings { get; set; } =
        new List<AkeneoVariantAxisMapping>();

    public int? AssociatedProductAttributeId { get; set; }

    public string AssociatedValueNameTemplate { get; set; } = "{axes}";

    public bool HideChildProductsWhenRepresentedByParent { get; set; } = true;
}