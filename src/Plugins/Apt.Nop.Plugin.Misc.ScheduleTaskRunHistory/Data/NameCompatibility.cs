using Apt.Nop.Plugin.Misc.ScheduleTaskRunHistory.Domain;
using Nop.Data.Mapping;

namespace Apt.Nop.Plugin.Misc.ScheduleTaskRunHistory.Data;

/// <summary>
/// Plugin table naming compatibility
/// </summary>
public partial class BaseNameCompatibility : INameCompatibility
{
    public Dictionary<Type, string> TableNames => new() { { typeof(ScheduleTaskRunRecord), $"Apt_{nameof(ScheduleTaskRunRecord)}" } };
    public Dictionary<(Type, string), string> ColumnName => new();
}