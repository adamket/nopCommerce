using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nop.Core;

namespace Apt.Nop.Plugin.Misc.NopCms.Domain;
public class TopicEntry : BaseEntity
{
    public int TopicId { get; set; }
    public string Title { get; set; }
    public string Body { get; set; }
    public int? CustomerId { get; set; }
    public DateTime CreatedOnUtc { get; set; }
    public string VersionName{ get; set; }
    public int Version { get; set; }
 
    public int TopicEntryStatusId { get; set; }
}


