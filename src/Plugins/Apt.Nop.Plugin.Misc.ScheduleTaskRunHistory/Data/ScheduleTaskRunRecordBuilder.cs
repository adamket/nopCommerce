using Apt.Nop.Plugin.Misc.ScheduleTaskRunHistory.Domain;
using FluentMigrator.Builders.Create.Table;
using Nop.Data.Mapping.Builders;

namespace Apt.Nop.Plugin.Misc.ScheduleTaskRunHistory.Data;
public class ScheduleTaskRunRecordBuilder : NopEntityBuilder<ScheduleTaskRunRecord>
{
    /// <summary>
    /// Apply entity configuration
    /// </summary>
    /// <param name="table">Create table expression builder</param>
    public override void MapEntity(CreateTableExpressionBuilder table)
    {
        table.WithColumn(nameof(ScheduleTaskRunRecord.Id)).AsInt32().Identity().PrimaryKey()
            .WithColumn(nameof(ScheduleTaskRunRecord.ScheduleTaskId)).AsInt32().NotNullable()
            .WithColumn(nameof(ScheduleTaskRunRecord.CompletedOnUtc)).AsDateTime().Nullable()
            .WithColumn(nameof(ScheduleTaskRunRecord.UpdatedOnUtc)).AsDateTime().Nullable()
            .WithColumn(nameof(ScheduleTaskRunRecord.CreatedOnUtc)).AsDateTime().NotNullable()
            .WithColumn(nameof(ScheduleTaskRunRecord.ErrorMessage)).AsString(int.MaxValue).Nullable();


    }
}