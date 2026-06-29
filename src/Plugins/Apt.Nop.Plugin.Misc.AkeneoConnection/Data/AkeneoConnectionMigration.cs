using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using FluentMigrator;
using Nop.Core;
using Nop.Data;
using Nop.Data.Extensions;
using Nop.Data.Mapping;
using Nop.Data.Migrations;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Data;

[NopMigration("2026-06-23 00:00:00", "AkeneoConnection: Create tables", MigrationProcessType.Installation)]
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
        CreateTable<AkeneoFamilyVariantImportConfiguration>();
        CreateTable<AkeneoFamilyVariantAxisMapping>();

        var configurationTable =
            NameCompatibilityManager.GetTableName(typeof(AkeneoFamilyVariantImportConfiguration));

        var axisMappingTable =
            NameCompatibilityManager.GetTableName(typeof(AkeneoFamilyVariantAxisMapping));

        Create.Index($"IX_{configurationTable}_AkeneoFamilyCode")
            .OnTable(configurationTable)
            .OnColumn(nameof(AkeneoFamilyVariantImportConfiguration.AkeneoFamilyCode))
            .Ascending()
            .WithOptions()
            .Unique();

        Create.Index($"IX_{axisMappingTable}_ConfigurationId")
            .OnTable(axisMappingTable)
            .OnColumn(nameof(AkeneoFamilyVariantAxisMapping.FamilyVariantImportConfigurationId))
            .Ascending();

    }

    public override void Down()
    {
        DropTopicChildTableAsync<AkeneoAttributeMapping>().Wait();
        DropTopicChildTableAsync<AkeneoNopEntityMapping>().Wait();
        DropTopicChildTableAsync<AkeneoSyncItemLog>().Wait();
        DropTopicChildTableAsync<AkeneoSyncProfile>().Wait();
        DropTopicChildTableAsync<AkeneoSyncRunRecord>().Wait();
        DropTopicChildTableAsync<AkeneoFamilyVariantImportConfiguration>().Wait();
        DropTopicChildTableAsync<AkeneoFamilyVariantAxisMapping>().Wait();
    }


    private void CreateTable<T>() where T : BaseEntity
    {
        var tableName = NameCompatibilityManager.GetTableName(typeof(T));

        if (!Schema.Table(tableName).Exists())
            Create.TableFor<T>();
    }

    private async Task DropTopicChildTableAsync<T>() where T : BaseEntity
    {
        var tableName = $"{AkeneoConnectionConstants.TablePrefix}{typeof(T).Name}";

        if (!Schema.Table(tableName).Exists())
            return;

        await dataProvider.ExecuteNonQueryAsync($"DROP TABLE [{tableName}]");
    }
}