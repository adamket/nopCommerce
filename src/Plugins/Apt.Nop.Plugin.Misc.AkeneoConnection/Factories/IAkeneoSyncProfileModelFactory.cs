using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models.SyncProfiles;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Factories;

public interface IAkeneoSyncProfileModelFactory
{
    Task<AkeneoSyncProfileListModel> PrepareListModelAsync();

    Task<AkeneoSyncProfileModel> PrepareModelAsync(
        AkeneoSyncProfile profile = null);

    Task PrepareAvailableOptionsAsync(
        AkeneoSyncProfileModel model);
}