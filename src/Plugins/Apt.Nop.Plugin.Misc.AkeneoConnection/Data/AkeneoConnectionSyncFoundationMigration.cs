//using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
//using FluentMigrator;
//using Nop.Data.Extensions;
//using Nop.Data.Mapping;
//using Nop.Data.Migrations;

//namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Data;

//[NopMigration("2026-07-15 02:00:00", "AkeneoConnection: Add sync state, ownership and run watermarks", MigrationProcessType.Update)]
//public class AkeneoConnectionSyncFoundationMigration : MigrationBase
//{
//    public override void Up()
//    {
//        EnsureRunRecordColumns();
//        EnsureProfileColumns();
//        EnsureSyncStateTable();
//        EnsureManagedRelationTable();
//        EnsureSyncLeaseTable();
//    }

//    public override void Down()
//    {
//        // Preserve synchronization history and ownership data.
//    }

//    private void EnsureRunRecordColumns()
//    {
//        var table = NameCompatibilityManager.GetTableName(typeof(AkeneoSyncRunRecord));

//        if (!Schema.Table(table).Column(nameof(AkeneoSyncRunRecord.SyncProfileId)).Exists())
//            Create.Column(nameof(AkeneoSyncRunRecord.SyncProfileId)).OnTable(table).AsInt32().Nullable();

//        if (!Schema.Table(table).Column(nameof(AkeneoSyncRunRecord.RunModeId)).Exists())
//            Create.Column(nameof(AkeneoSyncRunRecord.RunModeId)).OnTable(table).AsInt32().NotNullable().WithDefaultValue((int)AkeneoRunMode.Delta);

//        if (!Schema.Table(table).Column(nameof(AkeneoSyncRunRecord.WatermarkUtc)).Exists())
//            Create.Column(nameof(AkeneoSyncRunRecord.WatermarkUtc)).OnTable(table).AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);

//        if (!Schema.Table(table).Column(nameof(AkeneoSyncRunRecord.WarningCount)).Exists())
//            Create.Column(nameof(AkeneoSyncRunRecord.WarningCount)).OnTable(table).AsInt32().NotNullable().WithDefaultValue(0);

//        if (!Schema.Table(table).Column(nameof(AkeneoSyncRunRecord.CompletedAllPages)).Exists())
//            Create.Column(nameof(AkeneoSyncRunRecord.CompletedAllPages)).OnTable(table).AsBoolean().NotNullable().WithDefaultValue(false);

//        if (!Schema.Table(table).Column(nameof(AkeneoSyncRunRecord.WasTruncated)).Exists())
//            Create.Column(nameof(AkeneoSyncRunRecord.WasTruncated)).OnTable(table).AsBoolean().NotNullable().WithDefaultValue(false);

//        if (!Schema.Table(table).Column(nameof(AkeneoSyncRunRecord.ReconciliationCompleted)).Exists())
//            Create.Column(nameof(AkeneoSyncRunRecord.ReconciliationCompleted)).OnTable(table).AsBoolean().NotNullable().WithDefaultValue(false);

//        if (!Schema.Table(table).Column(nameof(AkeneoSyncRunRecord.ScopeHash)).Exists())
//            Create.Column(nameof(AkeneoSyncRunRecord.ScopeHash)).OnTable(table).AsString(64).Nullable();

//        if (!Schema.Table(table).Column(nameof(AkeneoSyncRunRecord.SearchJsonSnapshot)).Exists())
//            Create.Column(nameof(AkeneoSyncRunRecord.SearchJsonSnapshot)).OnTable(table).AsString(4000).Nullable();

//        if (!Schema.Table(table).Column(nameof(AkeneoSyncRunRecord.ProfileSnapshotJson)).Exists())
//            Create.Column(nameof(AkeneoSyncRunRecord.ProfileSnapshotJson)).OnTable(table).AsString(4000).Nullable();

//        if (!Schema.Table(table).Index($"IX_{table}_ProfileStatus").Exists())
//        {
//            Create.Index($"IX_{table}_ProfileStatus")
//                .OnTable(table)
//                .OnColumn(nameof(AkeneoSyncRunRecord.SyncProfileId)).Ascending()
//                .OnColumn(nameof(AkeneoSyncRunRecord.SyncStatusId)).Ascending()
//                .OnColumn(nameof(AkeneoSyncRunRecord.StartedOnUtc)).Descending();
//        }
//    }

//    private void EnsureProfileColumns()
//    {
//        var table = NameCompatibilityManager.GetTableName(typeof(AkeneoSyncProfile));

//        if (!Schema.Table(table).Column(nameof(AkeneoSyncProfile.MissingProductBehaviorId)).Exists())
//        {
//            Create.Column(nameof(AkeneoSyncProfile.MissingProductBehaviorId))
//                .OnTable(table)
//                .AsInt32()
//                .NotNullable()
//                .WithDefaultValue((int)AkeneoMissingProductBehavior.Ignore);
//        }
//    }

//    private void EnsureSyncStateTable()
//    {
//        var table = NameCompatibilityManager.GetTableName(typeof(AkeneoProductSyncState));

//        if (!Schema.Table(table).Exists())
//            Create.TableFor<AkeneoProductSyncState>();

//        Alter.Table(table).AlterColumn(nameof(AkeneoProductSyncState.AkeneoCode)).AsString(255).Nullable();
//        Alter.Table(table).AlterColumn(nameof(AkeneoProductSyncState.AkeneoUuid)).AsString(64).Nullable();
//        Alter.Table(table).AlterColumn(nameof(AkeneoProductSyncState.AkeneoParentCode)).AsString(255).Nullable();
//        Alter.Table(table).AlterColumn(nameof(AkeneoProductSyncState.LastDesiredStateHash)).AsString(64).Nullable();

//        if (!Schema.Table(table).Index($"IX_{table}_ProfileUuid").Exists())
//        {
//            Create.Index($"IX_{table}_ProfileUuid")
//                .OnTable(table)
//                .OnColumn(nameof(AkeneoProductSyncState.SyncProfileId)).Ascending()
//                .OnColumn(nameof(AkeneoProductSyncState.AkeneoEntityTypeId)).Ascending()
//                .OnColumn(nameof(AkeneoProductSyncState.AkeneoUuid)).Ascending();
//        }

//        if (!Schema.Table(table).Index($"IX_{table}_ProfileCode").Exists())
//        {
//            Create.Index($"IX_{table}_ProfileCode")
//                .OnTable(table)
//                .OnColumn(nameof(AkeneoProductSyncState.SyncProfileId)).Ascending()
//                .OnColumn(nameof(AkeneoProductSyncState.AkeneoEntityTypeId)).Ascending()
//                .OnColumn(nameof(AkeneoProductSyncState.AkeneoCode)).Ascending();
//        }

//        if (!Schema.Table(table).Index($"IX_{table}_LastSeen").Exists())
//        {
//            Create.Index($"IX_{table}_LastSeen")
//                .OnTable(table)
//                .OnColumn(nameof(AkeneoProductSyncState.SyncProfileId)).Ascending()
//                .OnColumn(nameof(AkeneoProductSyncState.LastSeenRunRecordId)).Ascending();
//        }
//    }

//    private void EnsureManagedRelationTable()
//    {
//        var table = NameCompatibilityManager.GetTableName(typeof(AkeneoManagedRelation));

//        if (!Schema.Table(table).Exists())
//            Create.TableFor<AkeneoManagedRelation>();

//        Alter.Table(table).AlterColumn(nameof(AkeneoManagedRelation.AkeneoAttributeCode)).AsString(255).Nullable();
//        Alter.Table(table).AlterColumn(nameof(AkeneoManagedRelation.AkeneoValueCode)).AsString(255).Nullable();

//        if (!Schema.Table(table).Index($"IX_{table}_ProductType").Exists())
//        {
//            Create.Index($"IX_{table}_ProductType")
//                .OnTable(table)
//                .OnColumn(nameof(AkeneoManagedRelation.SyncProfileId)).Ascending()
//                .OnColumn(nameof(AkeneoManagedRelation.NopProductId)).Ascending()
//                .OnColumn(nameof(AkeneoManagedRelation.RelationTypeId)).Ascending();
//        }

//        if (!Schema.Table(table).Index($"IX_{table}_RelationEntity").Exists())
//        {
//            Create.Index($"IX_{table}_RelationEntity")
//                .OnTable(table)
//                .OnColumn(nameof(AkeneoManagedRelation.SyncProfileId)).Ascending()
//                .OnColumn(nameof(AkeneoManagedRelation.RelationTypeId)).Ascending()
//                .OnColumn(nameof(AkeneoManagedRelation.NopRelationEntityId)).Ascending()
//                .WithOptions().Unique();
//        }
//    }

//    private void EnsureSyncLeaseTable()
//    {
//        var table = NameCompatibilityManager.GetTableName(typeof(AkeneoSyncLease));

//        if (!Schema.Table(table).Exists())
//            Create.TableFor<AkeneoSyncLease>();

//        Alter.Table(table).AlterColumn(nameof(AkeneoSyncLease.LockKey)).AsString(200).NotNullable();

//        if (!Schema.Table(table).Index($"IX_{table}_LockKey").Exists())
//        {
//            Create.Index($"IX_{table}_LockKey")
//                .OnTable(table)
//                .OnColumn(nameof(AkeneoSyncLease.LockKey)).Ascending()
//                .WithOptions().Unique();
//        }
//    }
//}
