using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Apt.Nop.Plugin.Misc.PageCache;
using Nop.Core.Caching;
using Nop.Core.Domain.Catalog;
using Nop.Core.Events;
using Nop.Services.Events;

namespace Apt.Nop.Plugin.Misc.PageCache.EventConsumers;
public class PageCacheEventConsumer(IStaticCacheManager cacheManager)
    : IConsumer<EntityUpdatedEvent<Product>>, IConsumer<EntityUpdatedEvent<Category>>, IConsumer<EntityUpdatedEvent<Manufacturer>>
{
    public async Task HandleEventAsync(EntityUpdatedEvent<Product> eventMessage)
    {
        await cacheManager.RemoveByPrefixAsync(string.Format(PageCacheConstants.PDP_CACHE_KEY_PREFIX,
            eventMessage.Entity.Id));
    }

    public async Task HandleEventAsync(EntityUpdatedEvent<Category> eventMessage)
    {
        await cacheManager.RemoveByPrefixAsync(string.Format(PageCacheConstants.CATEGORY_PAGE_CACHE_KEY_PREFIX,
            eventMessage.Entity.Id));
    }

    public async Task HandleEventAsync(EntityUpdatedEvent<Manufacturer> eventMessage)
    {
        await cacheManager.RemoveByPrefixAsync(string.Format(PageCacheConstants.MANUFACTURER_PAGE_CACHE_KEY_PREFIX,
            eventMessage.Entity.Id));
    }
}
