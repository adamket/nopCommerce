using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Nop.Core.Caching;
using Nop.Core.Configuration;
using Nop.Core.Infrastructure;
using Nop.Services.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

/// <summary>
/// Attribute values a sync unit wrote are cleared from the cache again once the
/// unit's transaction has finished, each attribute once.
/// </summary>
[TestFixture]
public class AkeneoAttributeValueCacheTrackerTests
{
    // CacheKey reads its default cache time from the app settings.
    [OneTimeSetUp]
    public void EnsureAppSettings()
    {
        if (Singleton<AppSettings>.Instance == null)
            Singleton<AppSettings>.Instance = new AppSettings(new List<IConfig> { new CacheConfig() });
    }

    [Test]
    public async Task Touched_attributes_are_cleared_once_by_exact_key()
    {
        var cache = new Mock<IStaticCacheManager>();
        var tracker = new AkeneoAttributeValueCacheTracker(cache.Object);

        tracker.Touch(112);
        tracker.Touch(112);
        tracker.Touch(404);
        tracker.Touch(0);
        await tracker.ClearTouchedAsync();
        await tracker.ClearTouchedAsync();

        cache.Verify(c => c.RemoveAsync(
            It.Is<CacheKey>(key => key.Key == NopCatalogDefaults.ProductAttributeValuesByAttributeCacheKey.Key),
            It.Is<object[]>(args => args.Length == 1 && (int)args[0] == 112)), Times.Once);
        cache.Verify(c => c.RemoveAsync(It.IsAny<CacheKey>(), It.Is<object[]>(args => (int)args[0] == 404)), Times.Once);
        cache.Verify(c => c.RemoveAsync(It.IsAny<CacheKey>(), It.IsAny<object[]>()), Times.Exactly(2));
    }
}
