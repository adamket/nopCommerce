using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using FluentMigrator;
using LinqToDB.Reflection;
using Nop.Core;
using Nop.Data;
using Nop.Data.Extensions;
using Nop.Data.Mapping;
using Nop.Data.Migrations;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Data;

[NopMigration("2026-07-10 00:00:00", "AkeneoConnection: Create tables", MigrationProcessType.Installation)]
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
        CreateTable<AkeneoFamilyMapping>();
        CreateTable<AkeneoFamilyVariantAxisMapping>();
        CreateTable<AkeneoFamilySubModelRule>();

        var configurationTable =
            NameCompatibilityManager.GetTableName(typeof(AkeneoFamilyMapping));

        var axisMappingTable =
            NameCompatibilityManager.GetTableName(typeof(AkeneoFamilyVariantAxisMapping));

        var subModelRuleTable =
            NameCompatibilityManager.GetTableName(
                typeof(AkeneoFamilySubModelRule));

        Create.Index($"IX_{configurationTable}_AkeneoFamilyCode")
            .OnTable(configurationTable)
            .OnColumn(nameof(AkeneoFamilyMapping.AkeneoFamilyCode))
            .Ascending()
            .WithOptions()
            .Unique();

        Create.Index($"IX_{axisMappingTable}_ConfigurationId")
            .OnTable(axisMappingTable)
            .OnColumn(nameof(AkeneoFamilyVariantAxisMapping.FamilyVariantImportConfigurationId))
            .Ascending();

        Create.Index($"IX_{subModelRuleTable}_FamilyMappingId")
            .OnTable(subModelRuleTable)
            .OnColumn(nameof(AkeneoFamilySubModelRule.FamilyMappingId))
            .Ascending();

    }

    public override void Down()
    {
        //do nothing
    }


    private void CreateTable<T>() where T : BaseEntity
    {
        var tableName = NameCompatibilityManager.GetTableName(typeof(T));

        if (!Schema.Table(tableName).Exists())
            Create.TableFor<T>();
    }


}