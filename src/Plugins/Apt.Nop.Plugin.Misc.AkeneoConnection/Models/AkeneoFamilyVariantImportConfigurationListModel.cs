using Nop.Web.Framework.Models;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Models;

public record AkeneoFamilyVariantImportConfigurationListModel : BaseNopModel
{
    public IList<AkeneoFamilyVariantImportConfigurationModel> Configurations { get; set; } =
        new List<AkeneoFamilyVariantImportConfigurationModel>();
}
