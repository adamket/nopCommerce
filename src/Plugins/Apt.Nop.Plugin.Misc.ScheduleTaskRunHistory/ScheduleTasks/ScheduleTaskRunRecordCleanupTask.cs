using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Apt.Nop.Plugin.Misc.ScheduleTaskRunHistory.Domain;
using Nop.Data;
using Nop.Services.Common;
using Nop.Services.ScheduleTasks;

namespace Apt.Nop.Plugin.Misc.ScheduleTaskRunHistory.ScheduleTasks
{
    public class ScheduleTaskRunRecordCleanupTask(IRepository<ScheduleTaskRunRecord> scheduleTaskRunRecordRepository, ScheduleTaskRunHistorySettings settings,
        IGenericAttributeService genericAttributeService, IScheduleTaskService scheduleTaskService) : IScheduleTask
    {
        public async Task ExecuteAsync()
        {
            var scheduleTasks = await scheduleTaskService.GetAllTasksAsync(true);
            var defaultRetentionDays = settings.DefaultRunRecordRetentionDays ?? 14; //default to two weeks

            foreach (var scheduleTask in scheduleTasks)
            {
                var retentionDays = await genericAttributeService.GetAttributeAsync<int>(scheduleTask,
                    ScheduleTaskRunHistoryConstants.RunRecordRetentionDaysGenericAttributeKey);
                if (retentionDays <= 0)
                {
                    retentionDays = defaultRetentionDays;
                }

                var deleteRecordsUpUntilDate = DateTime.UtcNow.AddDays(-retentionDays);

                var runRecordsToDelete = scheduleTaskRunRecordRepository.Table.Where(q => 
                    q.ScheduleTaskId == scheduleTask.Id
                     && q.CreatedOnUtc < deleteRecordsUpUntilDate).ToList();

                await scheduleTaskRunRecordRepository.DeleteAsync(runRecordsToDelete);
            }
        }
    }
}
