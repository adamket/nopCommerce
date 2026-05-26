using Nop.Core.Caching;

namespace Apt.Nop.Plugin.Misc.TopicsPlus;

public static class TopicsPlusConstants
{
    public const string PathToPlugin = "~/Plugins/Apt.Misc.TopicsPlus";

    public static class CacheKeys
    {
        public static CacheKey TopicDataByTopicIdCacheKey => new($"Nop.TopicData.by-topic-id.{{0}}");
        public static CacheKey AllTopicDataCacheKey => new($"Nop.AllTopicData");
    }
}