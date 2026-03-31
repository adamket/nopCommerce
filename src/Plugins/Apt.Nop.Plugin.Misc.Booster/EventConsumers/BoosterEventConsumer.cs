using Nop.Core.Caching;
using Nop.Core.Domain.Catalog;
using Nop.Core.Events;
using Nop.Services.Events;

namespace Apt.Nop.Plugin.Misc.Booster.EventConsumers;
public class BoosterEventConsumer(IStaticCacheManager cacheManager)
    : IConsumer<EntityUpdatedEvent<Product>>, IConsumer<EntityUpdatedEvent<Category>>, IConsumer<EntityUpdatedEvent<Manufacturer>>
{
    public async Task HandleEventAsync(EntityUpdatedEvent<Product> eventMessage)
    {
        await cacheManager.RemoveByPrefixAsync(string.Format(BoosterConstants.PDP_CACHE_KEY_PREFIX, eventMessage.Entity.Id));
    }

    public async Task HandleEventAsync(EntityUpdatedEvent<Category> eventMessage)
    {
        await cacheManager.RemoveByPrefixAsync(string.Format(BoosterConstants.CATEGORY_PAGE_CACHE_KEY_PREFIX, eventMessage.Entity.Id));
    }

    public async Task HandleEventAsync(EntityUpdatedEvent<Manufacturer> eventMessage)
    {
        await cacheManager.RemoveByPrefixAsync(string.Format(BoosterConstants.MANUFACTURER_PAGE_CACHE_KEY_PREFIX, eventMessage.Entity.Id));
    }
}
