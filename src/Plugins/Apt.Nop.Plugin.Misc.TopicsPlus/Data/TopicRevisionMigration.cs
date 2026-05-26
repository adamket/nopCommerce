using System.Data;
using Apt.Nop.Plugin.Misc.TopicsPlus.Domain;
using Apt.Nop.Plugin.Misc.TopicsPlus.ScheduleTasks;
using FluentMigrator;
using LinqToDB.DataProvider;
using Nop.Core;
using Nop.Core.Domain.ScheduleTasks;
using Nop.Data;
using Nop.Data.Extensions;
using Nop.Data.Migrations;

namespace Apt.Nop.Plugin.Misc.TopicsPlus.Data;

[NopMigration("2026-05-23 00:00:00", "TopicsPlus: Create TopicRevision table", MigrationProcessType.Installation)]
public class TopicRevisionMigration(INopDataProvider dataProvider) : MigrationBase
{
    public override void Up()
    {
        if (!DataSettingsManager.IsDatabaseInstalled())
            return;


        CreateTopicChildTable<TopicRevision>();
        CreateTopicChildTable<TopicDraft>();
        CreateTopicChildTable<TopicData>();
    }

    public override void Down()
    {
        DropTopicChildTableAsync<TopicRevision>().Wait();
        DropTopicChildTableAsync<TopicDraft>().Wait();
        DropTopicChildTableAsync<TopicData>().Wait();
    }


    private void CreateTopicChildTable<T>() where T : BaseEntity
    {
        var tableName = "Apt_" + typeof(T).Name;
        var foreignKeyName = $"FK_{tableName}_Topic_TopicId";

        if (!Schema.Table(tableName).Exists())
            Create.TableFor<T>();

        if (!Schema.Table(tableName).Constraint(foreignKeyName).Exists())
        {
            Create.ForeignKey(foreignKeyName)
                .FromTable(tableName)
                .ForeignColumn("TopicId")
                .ToTable("Topic")
                .PrimaryColumn("Id")
                .OnDelete(Rule.Cascade);
        }
    }

    private async Task DropTopicChildTableAsync<T>() where T : BaseEntity
    {
        var tableName = "Apt_" + typeof(T).Name;

        if (!Schema.Table(tableName).Exists())
            return;

        await dataProvider.ExecuteNonQueryAsync($"DROP TABLE [{tableName}]");
    }
}