using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nop.Web.Framework.Models;

namespace Apt.Nop.Plugin.Misc.NopCms.Models;
public record TopicEntrySearchModel : BaseSearchModel
{
    public int TopicId { get; set; }
}
