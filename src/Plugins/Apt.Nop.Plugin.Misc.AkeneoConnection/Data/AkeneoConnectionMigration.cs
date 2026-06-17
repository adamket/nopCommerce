using System.Data;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using FluentMigrator;
using Nop.Core;
using Nop.Data;
using Nop.Data.Extensions;
using Nop.Data.Migrations;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Data;

[NopMigration("2026-06-17 00:00:00", "AkeneoConnection: Create tables", MigrationProcessType.Installation)]
public class AkeneoConnectionMigration(INopDataProvider dataProvider) : MigrationBase
{
    public override void Up()
    {
        if (!DataSettingsManager.IsDatabaseInstalled())
            return;


        CreateTable<AkeneoAttributeMapping>();
        CreateTable<AkeneoNopEntityMapping>();
        CreateTable<AkeneoSyncItemLog>();
        CreateTable<AkeneoSyncProfile>();
        CreateTable<AkeneoSyncRunRecord>();
    }

    public override void Down()
    {
        DropTopicChildTableAsync<AkeneoAttributeMapping>().Wait();
        DropTopicChildTableAsync<AkeneoNopEntityMapping>().Wait();
        DropTopicChildTableAsync<AkeneoSyncItemLog>().Wait();
        DropTopicChildTableAsync<AkeneoSyncProfile>().Wait();
        DropTopicChildTableAsync<AkeneoSyncRunRecord>().Wait();
    }


    private void CreateTable<T>() where T : BaseEntity
    {
        var tableName = $"{AkeneoConstants.TablePrefix}{typeof(T).Name}";
      //  var foreignKeyName = $"FK_{tableName}_Topic_TopicId";

        if (!Schema.Table(tableName).Exists())
            Create.TableFor<T>();

        //if (!Schema.Table(tableName).Constraint(foreignKeyName).Exists())
        //{
        //    Create.ForeignKey(foreignKeyName)
        //        .FromTable(tableName)
        //        .ForeignColumn("TopicId")
        //        .ToTable("Topic")
        //        .PrimaryColumn("Id")
        //        .OnDelete(Rule.Cascade);
        //}
    }

    private async Task DropTopicChildTableAsync<T>() where T : BaseEntity
    {
        var tableName = $"{AkeneoConstants.TablePrefix}{typeof(T).Name}";

        if (!Schema.Table(tableName).Exists())
            return;

        await dataProvider.ExecuteNonQueryAsync($"DROP TABLE [{tableName}]");
    }
}