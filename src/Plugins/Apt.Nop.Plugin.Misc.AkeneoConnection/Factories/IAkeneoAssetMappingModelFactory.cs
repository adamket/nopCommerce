using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Factories;

public interface IAkeneoAssetMappingModelFactory
{
    Task<AkeneoAssetMappingListModel> PrepareListModelAsync(string familyCode = null);
    AkeneoAssetMappingModel PrepareMappingModel(
        AkeneoAssetMapping mapping,
        string selectedFamilyCode = null);
}
