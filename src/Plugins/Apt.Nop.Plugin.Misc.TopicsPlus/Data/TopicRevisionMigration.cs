using Apt.Nop.Plugin.Misc.TopicsPlus.Domain;
using Apt.Nop.Plugin.Misc.TopicsPlus.ScheduleTasks;
using FluentMigrator;
using LinqToDB.DataProvider;
using Nop.Core.Domain.ScheduleTasks;
using Nop.Data;
using Nop.Data.Extensions;
using Nop.Data.Migrations;

namespace Apt.Nop.Plugin.Misc.TopicsPlus.Data;

[NopMigration("2026-05-03 17:00:01", "TopicsPlus: Create TopicRevision table", MigrationProcessType.Installation)]
public class TopicRevisionMigration(INopDataProvider dataProvider) : MigrationBase
{
    public override void Up()
    {
        if (!DataSettingsManager.IsDatabaseInstalled())
            return;

        if (!Schema.Table(nameof(TopicRevision)).Exists())
            Create.TableFor<TopicRevision>();

     
    }

    public override void Down()
    {
    }
}