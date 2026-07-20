using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using FluentMigrator;
using Nop.Core;
using Nop.Data;
using Nop.Data.Extensions;
using Nop.Data.Mapping;
using Nop.Data.Migrations;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Data;

[NopMigration("2026-07-15 00:00:00", "AkeneoConnection: Create tables", MigrationProcessType.Installation)]
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
        CreateTable<AkeneoProductSyncState>();
        CreateTable<AkeneoManagedRelation>();
        CreateTable<AkeneoSyncLease>();

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


        var syncStateTable = NameCompatibilityManager.GetTableName(typeof(AkeneoProductSyncState));
        var managedRelationTable = NameCompatibilityManager.GetTableName(typeof(AkeneoManagedRelation));
        var syncLeaseTable = NameCompatibilityManager.GetTableName(typeof(AkeneoSyncLease));
        var runRecordTable = NameCompatibilityManager.GetTableName(typeof(AkeneoSyncRunRecord));

        Alter.Table(syncStateTable)
            .AlterColumn(nameof(AkeneoProductSyncState.AkeneoCode)).AsString(255).Nullable();
        Alter.Table(syncStateTable)
            .AlterColumn(nameof(AkeneoProductSyncState.AkeneoUuid)).AsString(64).Nullable();
        Alter.Table(syncStateTable)
            .AlterColumn(nameof(AkeneoProductSyncState.AkeneoParentCode)).AsString(255).Nullable();
        Alter.Table(syncStateTable)
            .AlterColumn(nameof(AkeneoProductSyncState.LastDesiredStateHash)).AsString(64).Nullable();

        Create.Index($"IX_{syncStateTable}_ProfileUuid")
            .OnTable(syncStateTable)
            .OnColumn(nameof(AkeneoProductSyncState.SyncProfileId)).Ascending()
            .OnColumn(nameof(AkeneoProductSyncState.AkeneoEntityTypeId)).Ascending()
            .OnColumn(nameof(AkeneoProductSyncState.AkeneoUuid)).Ascending();

        Create.Index($"IX_{syncStateTable}_ProfileCode")
            .OnTable(syncStateTable)
            .OnColumn(nameof(AkeneoProductSyncState.SyncProfileId)).Ascending()
            .OnColumn(nameof(AkeneoProductSyncState.AkeneoEntityTypeId)).Ascending()
            .OnColumn(nameof(AkeneoProductSyncState.AkeneoCode)).Ascending();

        Create.Index($"IX_{syncStateTable}_LastSeen")
            .OnTable(syncStateTable)
            .OnColumn(nameof(AkeneoProductSyncState.SyncProfileId)).Ascending()
            .OnColumn(nameof(AkeneoProductSyncState.LastSeenRunRecordId)).Ascending();

        Alter.Table(managedRelationTable)
            .AlterColumn(nameof(AkeneoManagedRelation.AkeneoAttributeCode)).AsString(255).Nullable();
        Alter.Table(managedRelationTable)
            .AlterColumn(nameof(AkeneoManagedRelation.AkeneoValueCode)).AsString(255).Nullable();

        Create.Index($"IX_{managedRelationTable}_ProductType")
            .OnTable(managedRelationTable)
            .OnColumn(nameof(AkeneoManagedRelation.SyncProfileId)).Ascending()
            .OnColumn(nameof(AkeneoManagedRelation.NopProductId)).Ascending()
            .OnColumn(nameof(AkeneoManagedRelation.RelationTypeId)).Ascending();

        Create.Index($"IX_{managedRelationTable}_RelationEntity")
            .OnTable(managedRelationTable)
            .OnColumn(nameof(AkeneoManagedRelation.SyncProfileId)).Ascending()
            .OnColumn(nameof(AkeneoManagedRelation.RelationTypeId)).Ascending()
            .OnColumn(nameof(AkeneoManagedRelation.NopRelationEntityId)).Ascending()
            .WithOptions().Unique();

        Alter.Table(syncLeaseTable)
            .AlterColumn(nameof(AkeneoSyncLease.LockKey)).AsString(200).NotNullable();

        Create.Index($"IX_{syncLeaseTable}_LockKey")
            .OnTable(syncLeaseTable)
            .OnColumn(nameof(AkeneoSyncLease.LockKey)).Ascending()
            .WithOptions().Unique();

        Alter.Table(runRecordTable)
            .AlterColumn(nameof(AkeneoSyncRunRecord.ScopeHash)).AsString(64).Nullable();

        Create.Index($"IX_{runRecordTable}_ProfileStatus")
            .OnTable(runRecordTable)
            .OnColumn(nameof(AkeneoSyncRunRecord.SyncProfileId)).Ascending()
            .OnColumn(nameof(AkeneoSyncRunRecord.SyncStatusId)).Ascending()
            .OnColumn(nameof(AkeneoSyncRunRecord.StartedOnUtc)).Descending();

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