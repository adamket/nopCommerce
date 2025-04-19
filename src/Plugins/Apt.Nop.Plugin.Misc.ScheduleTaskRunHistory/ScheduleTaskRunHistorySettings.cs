using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nop.Core.Configuration;

namespace Apt.Nop.Plugin.Misc.ScheduleTaskRunHistory
{
    public class ScheduleTaskRunHistorySettings : ISettings
    {
        public int? DefaultRunRecordRetentionDays { get; set; }
    }
}
