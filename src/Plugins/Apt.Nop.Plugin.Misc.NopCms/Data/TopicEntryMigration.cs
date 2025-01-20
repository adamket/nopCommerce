using Apt.Nop.Plugin.Misc.NopCms.Domain;
using FluentMigrator;
using Nop.Data;
using Nop.Data.Extensions;
using Nop.Data.Migrations;
using Nop.Services.Configuration;

namespace Apt.Nop.Plugin.Misc.NopCms.Data;

[NopMigration("2025-01-15 00:00:00", "Misc.NopCms 4.8.0", MigrationProcessType.Installation)]
public class TopicEntryMigration(ISettingService settingService) : MigrationBase
{
    #region Fields

    protected readonly ISettingService _settingService = settingService;

    #endregion

    #region Ctor

    #endregion

    #region Methods

    /// <summary>
    /// Collect the UP migration expressions
    /// </summary>
    public override async void Up()
    {
        if (!DataSettingsManager.IsDatabaseInstalled())
            return;


        if (!Schema.Schema("dbo").Table($"Apt_{nameof(TopicEntry)}").Exists())
        {
            Create.TableFor<TopicEntry>();
        }
    }

    public override void Down()
    {
        ;
    }

    #endregion
}