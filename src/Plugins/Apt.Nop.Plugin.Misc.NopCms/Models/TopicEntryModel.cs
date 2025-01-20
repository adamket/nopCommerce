using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nop.Web.Framework.Models;

namespace Apt.Nop.Plugin.Misc.NopCms.Models;
public record TopicEntryModel : BaseNopEntityModel
{
    public string Title { get; set; }
    public string CreatedOn { get; set; }
    public string CreatedBy { get; set; }
}
