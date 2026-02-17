using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nop.Core.Caching;
using Nop.Core.Domain.Catalog;
using Nop.Core.Events;
using Nop.Services.Events;

namespace Aperture.Nop.Plugin.Misc.PageCache.EventConsumers;
public class PageCacheEventConsumer : IConsumer<EntityUpdatedEvent<Product>>, IConsumer<EntityUpdatedEvent<Category>>
{
    private readonly IStaticCacheManager _cacheManager;

    public PageCacheEventConsumer(IStaticCacheManager cacheManager)
    {
        _cacheManager = cacheManager;
    }

    public async Task HandleEventAsync(EntityUpdatedEvent<Product> eventMessage)
    {
        await _cacheManager.RemoveByPrefixAsync(string.Format(PageCacheConstants.PDP_CACHE_KEY_PREFIX,
            eventMessage.Entity.Id));
    }

    public async Task HandleEventAsync(EntityUpdatedEvent<Category> eventMessage)
    {
        await _cacheManager.RemoveByPrefixAsync(string.Format(PageCacheConstants.CATEGORY_PAGE_CACHE_KEY_PREFIX,
            eventMessage.Entity.Id));
    }
}
