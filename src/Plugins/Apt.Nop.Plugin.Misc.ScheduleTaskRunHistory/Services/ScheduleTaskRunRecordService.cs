using Apt.Nop.Plugin.Misc.ScheduleTaskRunHistory.Domain;
using Nop.Core;
using Nop.Data;

namespace Apt.Nop.Plugin.Misc.ScheduleTaskRunHistory.Services
{
    public class ScheduleTaskRunRecordService(IRepository<ScheduleTaskRunRecord> scheduleTaskRunRecordRepository)
    {
        public async Task InsertScheduleTaskRunRecordAsync(ScheduleTaskRunRecord scheduleTaskRunRecord)
        {
            await scheduleTaskRunRecordRepository.InsertAsync(scheduleTaskRunRecord);
        }

        public async Task UpdateScheduleTaskRunRecordAsync(ScheduleTaskRunRecord scheduleTaskRunRecord)
        {
            await scheduleTaskRunRecordRepository.UpdateAsync(scheduleTaskRunRecord);
        }

        public ScheduleTaskRunRecord GetScheduleTaskRunRecordById(int id)
        {
            return scheduleTaskRunRecordRepository.Table.FirstOrDefault(q => q.Id == id);
        }

        public async Task<IPagedList<ScheduleTaskRunRecord>> SearchScheduleTaskRunRecordsAsync(int? taskId = null,
            DateTime? createdFromDateUtc = null,
            DateTime? createdToDateUtc = null, int pageIndex = 0, int pageSize = int.MaxValue)

        {
            var records = scheduleTaskRunRecordRepository.Table;
            if ((taskId ?? 0) > 0)
            {
                records = records.Where(q => q.ScheduleTaskId == taskId);
            }

            if (createdFromDateUtc.HasValue)
            {
                records = records.Where(q => q.CreatedOnUtc >= createdFromDateUtc.Value);
            }

            if (createdToDateUtc.HasValue)
            {
                records = records.Where(q => q.CreatedOnUtc <= createdToDateUtc.Value);
            }

            records = records.OrderByDescending(q => q.CreatedOnUtc);
            var result = await records.ToPagedListAsync(pageIndex, pageSize);
            return result;
        }
    }

}
