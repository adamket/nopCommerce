using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nop.Core;
using Nop.Core.Caching;
using Nop.Core.Domain.Common;
using Nop.Core.Events;
using Nop.Data;
using Nop.Services.Common;

namespace Apt.Nop.Plugin.Misc.OneTimePasscode.Services;
public class CustomGenericAttributeService(
    IRepository<GenericAttribute> genericAttributeRepository,
    IShortTermCacheManager shortTermCacheManager,
    IStaticCacheManager staticCacheManager)
    : GenericAttributeService(genericAttributeRepository, shortTermCacheManager, staticCacheManager),
        ICustomGenericAttributeService
{
    public async Task<(TPropType, DateTime?)> GetAttributeWithCreateUpdateDateAsync<TPropType>(BaseEntity entity, string key, int storeId = 0,
        TPropType defaultValue = default)
    {
        if (entity == null)
            throw new ArgumentNullException(nameof(entity));

        var keyGroup = entity.GetType().Name;

        var props = await GetAttributesForEntityAsync(entity.Id, keyGroup);

        if (props == null)
            return (defaultValue, null);

        props = props.Where(x => x.StoreId == storeId).ToList();
        if (!props.Any())
            return (defaultValue, null);

        var prop = props.FirstOrDefault(ga =>
            ga.Key.Equals(key, StringComparison.InvariantCultureIgnoreCase)); 

        if (prop == null || string.IsNullOrEmpty(prop.Value))
            return (defaultValue, null);

        return (CommonHelper.To<TPropType>(prop.Value), prop.CreatedOrUpdatedDateUTC);
    }
}
