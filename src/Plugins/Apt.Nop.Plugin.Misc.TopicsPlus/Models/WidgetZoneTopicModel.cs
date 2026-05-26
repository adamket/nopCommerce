using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nop.Web.Models.Topics;

namespace Apt.Nop.Plugin.Misc.TopicsPlus.Models;
public record WidgetZoneTopicModel
{
    public IList<TopicModel> Topics { get; set; }
}

