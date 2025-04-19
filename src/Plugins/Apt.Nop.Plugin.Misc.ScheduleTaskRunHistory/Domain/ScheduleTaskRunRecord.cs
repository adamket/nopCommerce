using Nop.Core;

namespace Apt.Nop.Plugin.Misc.ScheduleTaskRunHistory.Domain
{
    public class ScheduleTaskRunRecord : BaseEntity
    {
        public DateTime CreatedOnUtc { get; set; }
        public DateTime? UpdatedOnUtc { get; set; }
        public DateTime? CompletedOnUtc { get; set; }
      
        public int ScheduleTaskId { get; set; }
        public string ErrorMessage { get; set; }
    }
}
