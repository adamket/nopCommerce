using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Apt.Nop.Plugin.AkeneoConnection.Domain;
using Nop.Data;

namespace Apt.Nop.Plugin.AkeneoConnection.Services;
public class AkeneoSyncProfileService(IRepository<AkeneoSyncProfile> syncProfileRepository) : IAkeneoSyncProfileService
{

    public async Task InsertAkeneoSyncProfileAsync(AkeneoSyncProfile syncProfile)
    {
        await syncProfileRepository.InsertAsync(syncProfile);
    }

    public async Task UpdateAkeneoSyncProfileAsync(AkeneoSyncProfile syncProfile)
    {
        await syncProfileRepository.UpdateAsync(syncProfile);
    }

    public async Task DeleteAkeneoSyncProfileAsync(AkeneoSyncProfile syncProfile)
    {
        await syncProfileRepository.DeleteAsync(syncProfile);
    }
}
