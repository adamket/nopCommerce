using Apt.Nop.Plugin.Misc.ScheduleTaskRunHistory.Domain;
using FluentMigrator;
using Nop.Data;
using Nop.Data.Extensions;
using Nop.Data.Migrations;
using Nop.Services.Configuration;

namespace Apt.Nop.Plugin.Misc.ScheduleTaskRunHistory.Data;

[NopMigration("2025-01-15 00:00:00", "Misc.ScheduleTaskRunHistory 4.8.0", MigrationProcessType.Installation)]
public class ScheduleTaskRunRecordMigration(ISettingService settingService) : MigrationBase
{
    #region Fields

    protected readonly ISettingService _settingService = settingService;

    #endregion


    #region Methods

    /// <summary>
    /// Collect the UP migration expressions
    /// </summary>
    public override async void Up()
    {
        if (!DataSettingsManager.IsDatabaseInstalled())
            return;


        if (!Schema.Schema("dbo").Table($"Apt_{nameof(ScheduleTaskRunRecord)}").Exists())
        {
            Create.TableFor<ScheduleTaskRunRecord>();
        }
    }

    public override void Down()
    {
        ;
    }

    #endregion
}