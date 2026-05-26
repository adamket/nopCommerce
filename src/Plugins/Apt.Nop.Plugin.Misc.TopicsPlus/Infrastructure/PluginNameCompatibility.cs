using Apt.Nop.Plugin.Misc.TopicsPlus.Domain;
using Nop.Data.Mapping;

namespace Apt.Nop.Plugin.Misc.TopicsPlus.Infrastructure
{
    public partial class PluginNameCompatibility : INameCompatibility
    {
        public Dictionary<Type, string> TableNames => new Dictionary<Type, string>
        {
            { typeof(TopicRevision), "Apt_TopicRevision" },
            { typeof(TopicDraft), "Apt_TopicDraft" },
            { typeof(TopicData), "Apt_TopicData" },
        };

        public Dictionary<(Type, string), string> ColumnName => new Dictionary<(Type, string), string>();
    }
}