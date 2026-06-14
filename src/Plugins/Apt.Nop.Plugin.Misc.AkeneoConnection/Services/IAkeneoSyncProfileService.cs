using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
public interface IAkeneoSyncProfileService
{
    Task InsertAkeneoSyncProfileAsync(AkeneoSyncProfile syncProfile);
    Task UpdateAkeneoSyncProfileAsync(AkeneoSyncProfile syncProfile);
    Task DeleteAkeneoSyncProfileAsync(AkeneoSyncProfile syncProfile);
}
