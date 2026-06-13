using Apt.Nop.Plugin.AkeneoConnection.Domain;

namespace Apt.Nop.Plugin.AkeneoConnection.Services;
public interface IAkeneoSyncProfileService
{
    Task InsertAkeneoSyncProfileAsync(AkeneoSyncProfile syncProfile);
    Task UpdateAkeneoSyncProfileAsync(AkeneoSyncProfile syncProfile);
    Task DeleteAkeneoSyncProfileAsync(AkeneoSyncProfile syncProfile);
}
