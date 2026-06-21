using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Factories;
public interface IAkeneoFamilyVariantImportConfigurationModelFactory
{
    Task<AkeneoFamilyVariantImportConfigurationListModel> PrepareListModelAsync();

    Task<AkeneoFamilyVariantImportConfigurationModel> PrepareModelAsync(
        AkeneoFamilyVariantImportConfigurationModel model = null,
        AkeneoFamilyVariantImportConfiguration configuration = null);
}
