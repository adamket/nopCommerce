using Nop.Core.Caching;

namespace Apt.Nop.Plugin.Misc.TopicsPlus;

public static class TopicsPlusConstants
{
    public const string PathToPlugin = "~/Plugins/Apt.Misc.TopicsPlus";
    public const string HasTokensPropertyName = "tplus-has-tokens";

    public static class CacheKeys
    {
        public static CacheKey TopicDataByTopicIdCacheKey => new($"Nop.TopicData.by-topic-id.{{0}}");
        public static CacheKey AllTopicDataCacheKey => new($"Nop.AllTopicData");

        public static string PreparedContentPrefix = "TopicsPlus.Content.{0}"; //topic id
        public static CacheKey PreparedContentCacheKey => new ($"{PreparedContentPrefix}.{{1}}.{{2}}"); // base entity id, base entity type, language id
    }
}