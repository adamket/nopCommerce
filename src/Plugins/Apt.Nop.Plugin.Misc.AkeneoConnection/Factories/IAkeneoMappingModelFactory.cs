using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Factories;
public interface IAkeneoMappingModelFactory
{
    Task<AkeneoAttributeMappingListModel> PrepareAttributeMappingListModelAsync();

    Task<AkeneoAttributeMappingListModel> PrepareAttributeMappingListModelAsync(
        AkeneoAttributeMappingListModel model);
}
