using Apt.Nop.Plugin.Misc.Booster.Services;
using Microsoft.Extensions.DependencyInjection;
using Nop.Core.Caching;
using Nop.Core.Domain.Catalog;
using Nop.Core.Events;
using Nop.Core.Infrastructure;
using Nop.Services.Events;

namespace Apt.Nop.Plugin.Misc.Booster.EventConsumers;
public class BoosterEventConsumer(IDebouncer debouncer)
    : IConsumer<EntityUpdatedEvent<Product>>, IConsumer<EntityUpdatedEvent<Category>>, IConsumer<EntityUpdatedEvent<Manufacturer>>
{
    public async Task HandleEventAsync(EntityUpdatedEvent<Product> eventMessage)
    {
        //TODO - prevent update on every order placed?
        debouncer.Debounce(
            key: $"{nameof(Product)}-cache-clear-" + eventMessage.Entity.Id,
            action: async ct =>
            {
                using var scope = EngineContext.Current
                    .Resolve<IServiceProvider>()
                    .CreateScope();

                var cacheManager = scope.ServiceProvider.GetRequiredService<IStaticCacheManager>();
                await cacheManager.RemoveByPrefixAsync(string.Format(BoosterConstants.PDP_CACHE_KEY_PREFIX,
                    eventMessage.Entity.Id));
            },
            delay: TimeSpan.FromSeconds(1)
        );
    }

    public async Task HandleEventAsync(EntityUpdatedEvent<Category> eventMessage)
    {
        debouncer.Debounce(
            key: $"{nameof(Category)}-cache-clear-" + eventMessage.Entity.Id,
            action: async ct =>
            {
                using var scope = EngineContext.Current
                    .Resolve<IServiceProvider>()
                    .CreateScope();

                var cacheManager = scope.ServiceProvider.GetRequiredService<IStaticCacheManager>();
                await cacheManager.RemoveByPrefixAsync((string.Format(BoosterConstants.CATEGORY_PAGE_CACHE_KEY_PREFIX,
                    eventMessage.Entity.Id)));
            },
            delay: TimeSpan.FromSeconds(1)
        );
    }

    public async Task HandleEventAsync(EntityUpdatedEvent<Manufacturer> eventMessage)
    {
        debouncer.Debounce(
            key: $"{nameof(Manufacturer)}-cache-clear-" + eventMessage.Entity.Id,
            action: async ct =>
            {
                using var scope = EngineContext.Current
                    .Resolve<IServiceProvider>()
                    .CreateScope();

                var cacheManager = scope.ServiceProvider.GetRequiredService<IStaticCacheManager>();
                await cacheManager.RemoveByPrefixAsync(string.Format(BoosterConstants.MANUFACTURER_PAGE_CACHE_KEY_PREFIX,
                    eventMessage.Entity.Id));
            },
            delay: TimeSpan.FromSeconds(1)
        );

    }
}
