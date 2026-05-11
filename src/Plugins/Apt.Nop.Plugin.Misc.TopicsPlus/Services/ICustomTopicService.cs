using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nop.Core.Domain.Topics;
using Nop.Services.Topics;

namespace Apt.Nop.Plugin.Misc.TopicsPlus.Services;
public interface ICustomTopicService : ITopicService
{
    Task UpdateTopicAsync(Topic topic, bool publishEvent = true);
}
