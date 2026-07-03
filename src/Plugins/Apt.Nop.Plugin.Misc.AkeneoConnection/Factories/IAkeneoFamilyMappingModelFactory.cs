using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Factories;
public interface IAkeneoFamilyMappingModelFactory
{
    Task<AkeneoFamilyVariantImportConfigurationListModel> PrepareListModelAsync();

    Task<AkeneoFamilyVariantImportConfigurationModel> PrepareModelAsync(
        AkeneoFamilyVariantImportConfigurationModel model = null,
        AkeneoFamilyMapping configuration = null);
}
