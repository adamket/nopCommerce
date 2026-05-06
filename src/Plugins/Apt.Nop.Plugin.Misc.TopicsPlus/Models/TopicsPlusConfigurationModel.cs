using Nop.Web.Framework.Models;
using Nop.Web.Framework.Mvc.ModelBinding;

namespace Apt.Nop.Plugin.Misc.TopicsPlus.Models;

public record TopicsPlusConfigurationModel : BaseNopModel
{
    public TopicsPlusConfigurationModel()
    {
    
    }

    public int ActiveStoreScopeConfiguration { get; set; }

    [NopResourceDisplayName("Plugins.Misc.TopicsPlus.RevisionRetentionDays")]
    public int RevisionRetentionDays { get; set; }
    public bool RevisionRetentionDays_OverrideForStore { get; set; }


}