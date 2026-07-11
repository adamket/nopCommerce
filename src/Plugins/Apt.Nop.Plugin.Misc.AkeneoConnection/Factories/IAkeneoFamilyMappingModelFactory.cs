using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Factories;
public interface IAkeneoFamilyMappingModelFactory
{
    Task<AkeneoFamilyMappingListModel> PrepareListModelAsync();

    Task<AkeneoFamilyMappingModel> PrepareModelAsync(
        AkeneoFamilyMappingModel model = null,
        AkeneoFamilyMapping configuration = null);
}
