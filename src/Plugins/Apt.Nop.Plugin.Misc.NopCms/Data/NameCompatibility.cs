using Apt.Nop.Plugin.Misc.NopCms.Domain;
using Nop.Data.Mapping;

namespace Apt.Nop.Plugin.Misc.NopCms.Data;

/// <summary>
/// Plugin table naming compatibility
/// </summary>
public partial class BaseNameCompatibility : INameCompatibility
{
    public Dictionary<Type, string> TableNames => new() { { typeof(TopicEntry), $"Apt_{nameof(TopicEntry)}" } };
    public Dictionary<(Type, string), string> ColumnName => new();
}