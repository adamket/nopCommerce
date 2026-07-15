using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Factories;
public interface IAkeneoAttributeMappingModelFactory
{
    Task<AkeneoAttributeMappingListModel> PrepareAttributeMappingListModelAsync(string akeneoFamilyCode = null);

    Task<AkeneoAttributeMappingListModel> PrepareAttributeMappingListModelAsync(
        AkeneoAttributeMappingListModel model);
}
