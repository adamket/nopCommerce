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


        var mappingTable = NameCompatibilityManager.GetTableName(typeof(AkeneoNopEntityMapping));
        var itemLogTable = NameCompatibilityManager.GetTableName(typeof(AkeneoSyncItemLog));

        // nvarchar(max) can't participate in an index — constrain first.
        // Option mapping codes are "{productKey}:{attributeCode}:{option}", so 400 is generous.
        Alter.Table(mappingTable)
            .AlterColumn(nameof(AkeneoNopEntityMapping.AkeneoCode)).AsString(400).Nullable();

        Alter.Table(mappingTable)
            .AlterColumn(nameof(AkeneoNopEntityMapping.AkeneoUuid)).AsString(64).Nullable();

        if (!Schema.Table(mappingTable).Index($"IX_{mappingTable}_TypeCode").Exists())
        {
            Create.Index($"IX_{mappingTable}_TypeCode")
                .OnTable(mappingTable)
                .OnColumn(nameof(AkeneoNopEntityMapping.AkeneoEntityTypeId)).Ascending()
                .OnColumn(nameof(AkeneoNopEntityMapping.NopEntityTypeId)).Ascending()
                .OnColumn(nameof(AkeneoNopEntityMapping.AkeneoCode)).Ascending();
        }

        if (!Schema.Table(mappingTable).Index($"IX_{mappingTable}_TypeUuid").Exists())
        {
            Create.Index($"IX_{mappingTable}_TypeUuid")
                .OnTable(mappingTable)
                .OnColumn(nameof(AkeneoNopEntityMapping.AkeneoEntityTypeId)).Ascending()
                .OnColumn(nameof(AkeneoNopEntityMapping.NopEntityTypeId)).Ascending()
                .OnColumn(nameof(AkeneoNopEntityMapping.AkeneoUuid)).Ascending();
        }

        if (!Schema.Table(itemLogTable).Index($"IX_{itemLogTable}_SyncRunRecordId").Exists())
        {
            Create.Index($"IX_{itemLogTable}_SyncRunRecordId")
                .OnTable(itemLogTable)
                .OnColumn(nameof(AkeneoSyncItemLog.SyncRunRecordId)).Ascending();
        }

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