using Nop.Core.Configuration;

namespace Apt.Nop.Plugin.Misc.TopicsPlus;

/// <summary>
/// Represents settings of "Check money order" payment plugin
/// </summary>
public class TopicsPlusSettings : ISettings
{
    public int RevisionRetentionDays { get; set; }
}