using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using FluentMigrator;
using Nop.Core;
using Nop.Data;
using Nop.Data.Extensions;
using Nop.Data.Mapping;
using Nop.Data.Migrations;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Data;

[NopMigration(
    "2026-07-21 01:00:00",
    "AkeneoConnection: Add managed asset synchronization",
    MigrationProcessType.NoMatter)]
public sealed class AkeneoConnectionMigration : MigrationBase
{
    public override void Up()
    {
        if (!DataSettingsManager.IsDatabaseInstalled())
            return;

        EnsureTables();

        EnsureAttributeMappingColumns();
        EnsureAttributeMappingFallbackSourceSchema();
        EnsureFamilyMappingColumns();
        EnsureAssetMappingSchema();
        EnsureManagedAssetSchema();
        EnsureSyncProfileColumns();
        EnsureSyncRunRecordColumns();

        EnsureFamilyMappingIndexes();
        EnsureEntityMappingSchema();
        EnsureSyncItemLogSchema();
        EnsureProductSyncStateSchema();
        EnsureManagedRelationSchema();
        EnsureSyncLeaseSchema();
        EnsureSyncRunRecordIndexes();
    }

    public override void Down()
    {
        // Intentionally preserve Akeneo configuration, mappings,
        // synchronization state, ownership data, and run history.
    }

    #region Tables

    private void EnsureTables()
    {
        EnsureTable<AkeneoAttributeMapping>();
        EnsureTable<AkeneoAttributeMappingFallbackSource>();
        EnsureTable<AkeneoNopEntityMapping>();
        EnsureTable<AkeneoSyncItemLog>();
        EnsureTable<AkeneoSyncProfile>();
        EnsureTable<AkeneoSyncRunRecord>();
        EnsureTable<AkeneoFamilyMapping>();
        EnsureTable<AkeneoFamilyVariantAxisMapping>();
        EnsureTable<AkeneoFamilySubModelRule>();
        EnsureTable<AkeneoProductSyncState>();
        EnsureTable<AkeneoManagedRelation>();
        EnsureTable<AkeneoAssetMapping>();
        EnsureTable<AkeneoManagedAsset>();
        EnsureTable<AkeneoSyncLease>();
    }

    private void EnsureTable<T>() where T : BaseEntity
    {
        var table = GetTableName<T>();

        if (!Schema.Table(table).Exists())
            Create.TableFor<T>();
    }

    #endregion

    #region Attribute mappings

    private void EnsureAttributeMappingColumns()
    {
        var table = GetTableName<AkeneoAttributeMapping>();

        // Computed mappings do not have one primary Akeneo attribute, so the
        // source code must be nullable. Akeneo attribute codes are limited and
        // do not need an unbounded text column.
        Alter.Table(table)
            .AlterColumn(nameof(AkeneoAttributeMapping.AkeneoAttributeCode))
            .AsString(255)
            .Nullable();

        EnsureNullableStringColumn(
            table,
            nameof(AkeneoAttributeMapping.AkeneoReferenceEntityCode),
            255);

        EnsureNullableStringColumn(
            table,
            nameof(
                AkeneoAttributeMapping
                    .AkeneoReferenceEntityAttributeCode),
            255);

        EnsureNullableStringColumn(
            table,
            nameof(AkeneoAttributeMapping.MappingKey),
            100);

        EnsureNullableStringColumn(
            table,
            nameof(AkeneoAttributeMapping.Name),
            255);

        EnsureNullableStringColumn(
            table,
            nameof(AkeneoAttributeMapping.ValueTemplate),
            4000);

        if (!ColumnExists(
                table,
                nameof(AkeneoAttributeMapping.ValueModeId)))
        {
            Create.Column(nameof(AkeneoAttributeMapping.ValueModeId))
                .OnTable(table)
                .AsInt32()
                .NotNullable()
                .WithDefaultValue(
                    (int)AkeneoAttributeMappingValueMode.SingleAttribute);
        }

        if (!ColumnExists(
                table,
                nameof(AkeneoAttributeMapping.EntityScopeId)))
        {
            Create.Column(nameof(AkeneoAttributeMapping.EntityScopeId))
                .OnTable(table)
                .AsInt32()
                .NotNullable()
                .WithDefaultValue(
                    (int)AkeneoAttributeMappingEntityScope.All);
        }

        if (!ColumnExists(
                table,
                nameof(
                    AkeneoAttributeMapping
                        .SpecificationMissingValueHandlingId)))
        {
            Create.Column(
                    nameof(
                        AkeneoAttributeMapping
                            .SpecificationMissingValueHandlingId))
                .OnTable(table)
                .AsInt32()
                .NotNullable()
                .WithDefaultValue(
                    (int)AkeneoSpecificationMissingValueHandling
                        .CreateSpecificationAttributeOption);
        }
    }

    #endregion


    private void EnsureAttributeMappingFallbackSourceSchema()
    {
        var table = GetTableName<AkeneoAttributeMappingFallbackSource>();

        Alter.Table(table)
            .AlterColumn(
                nameof(
                    AkeneoAttributeMappingFallbackSource
                        .AkeneoAttributeCode))
            .AsString(255)
            .NotNullable();

        Alter.Table(table)
            .AlterColumn(
                nameof(
                    AkeneoAttributeMappingFallbackSource
                        .AkeneoReferenceEntityCode))
            .AsString(255)
            .Nullable();

        Alter.Table(table)
            .AlterColumn(
                nameof(
                    AkeneoAttributeMappingFallbackSource
                        .AkeneoReferenceEntityAttributeCode))
            .AsString(255)
            .Nullable();

        var indexName = $"IX_{table}_MappingOrder";

        if (!IndexExists(table, indexName))
        {
            Create.Index(indexName)
                .OnTable(table)
                .OnColumn(
                    nameof(
                        AkeneoAttributeMappingFallbackSource
                            .AttributeMappingId))
                .Ascending()
                .OnColumn(
                    nameof(
                        AkeneoAttributeMappingFallbackSource
                            .DisplayOrder))
                .Ascending();
        }
    }

    #region Family mappings

    private void EnsureFamilyMappingColumns()
    {
        var table = GetTableName<AkeneoFamilyMapping>();

        if (!ColumnExists(
                table,
                nameof(
                    AkeneoFamilyMapping
                        .ProductModelHierarchyModeId)))
        {
            Create.Column(
                    nameof(
                        AkeneoFamilyMapping
                            .ProductModelHierarchyModeId))
                .OnTable(table)
                .AsInt32()
                .NotNullable()
                .WithDefaultValue(
                    (int)AkeneoProductModelHierarchyMode
                        .ImmediateParentProductModel);
        }
    }

    private void EnsureFamilyMappingIndexes()
    {
        EnsureFamilyCodeIndex();
        EnsureAxisMappingConfigurationIndex();
        EnsureSubModelRuleFamilyMappingIndex();
    }

    private void EnsureFamilyCodeIndex()
    {
        var table = GetTableName<AkeneoFamilyMapping>();
        var indexName = $"IX_{table}_AkeneoFamilyCode";

        if (IndexExists(table, indexName))
            return;

        Create.Index(indexName)
            .OnTable(table)
            .OnColumn(nameof(AkeneoFamilyMapping.AkeneoFamilyCode))
            .Ascending()
            .WithOptions()
            .Unique();
    }

    private void EnsureAxisMappingConfigurationIndex()
    {
        var table = GetTableName<AkeneoFamilyVariantAxisMapping>();
        var indexName = $"IX_{table}_ConfigurationId";

        if (IndexExists(table, indexName))
            return;

        Create.Index(indexName)
            .OnTable(table)
            .OnColumn(
                nameof(
                    AkeneoFamilyVariantAxisMapping
                        .FamilyVariantImportConfigurationId))
            .Ascending();
    }

    private void EnsureSubModelRuleFamilyMappingIndex()
    {
        var table = GetTableName<AkeneoFamilySubModelRule>();
        var indexName = $"IX_{table}_FamilyMappingId";

        if (IndexExists(table, indexName))
            return;

        Create.Index(indexName)
            .OnTable(table)
            .OnColumn(
                nameof(AkeneoFamilySubModelRule.FamilyMappingId))
            .Ascending();
    }

    #endregion

    #region Entity mappings

    private void EnsureEntityMappingSchema()
    {
        var table = GetTableName<AkeneoNopEntityMapping>();

        Alter.Table(table)
            .AlterColumn(nameof(AkeneoNopEntityMapping.AkeneoCode))
            .AsString(400)
            .Nullable();

        Alter.Table(table)
            .AlterColumn(nameof(AkeneoNopEntityMapping.AkeneoUuid))
            .AsString(64)
            .Nullable();

        EnsureEntityMappingTypeCodeIndex(table);
        EnsureEntityMappingTypeUuidIndex(table);
    }

    private void EnsureEntityMappingTypeCodeIndex(string table)
    {
        var indexName = $"IX_{table}_TypeCode";

        if (IndexExists(table, indexName))
            return;

        Create.Index(indexName)
            .OnTable(table)
            .OnColumn(
                nameof(
                    AkeneoNopEntityMapping.AkeneoEntityTypeId))
            .Ascending()
            .OnColumn(
                nameof(
                    AkeneoNopEntityMapping.NopEntityTypeId))
            .Ascending()
            .OnColumn(nameof(AkeneoNopEntityMapping.AkeneoCode))
            .Ascending();
    }

    private void EnsureEntityMappingTypeUuidIndex(string table)
    {
        var indexName = $"IX_{table}_TypeUuid";

        if (IndexExists(table, indexName))
            return;

        Create.Index(indexName)
            .OnTable(table)
            .OnColumn(
                nameof(
                    AkeneoNopEntityMapping.AkeneoEntityTypeId))
            .Ascending()
            .OnColumn(
                nameof(
                    AkeneoNopEntityMapping.NopEntityTypeId))
            .Ascending()
            .OnColumn(nameof(AkeneoNopEntityMapping.AkeneoUuid))
            .Ascending();
    }

    #endregion

    #region Sync item logs

    private void EnsureSyncItemLogSchema()
    {
        var table = GetTableName<AkeneoSyncItemLog>();
        var indexName = $"IX_{table}_SyncRunRecordId";

        if (IndexExists(table, indexName))
            return;

        Create.Index(indexName)
            .OnTable(table)
            .OnColumn(nameof(AkeneoSyncItemLog.SyncRunRecordId))
            .Ascending();
    }

    #endregion

    #region Asset mappings

    private void EnsureAssetMappingSchema()
    {
        var table = GetTableName<AkeneoAssetMapping>();

        Alter.Table(table).AlterColumn(nameof(AkeneoAssetMapping.MappingKey)).AsString(100).NotNullable();
        Alter.Table(table).AlterColumn(nameof(AkeneoAssetMapping.Name)).AsString(255).NotNullable();
        Alter.Table(table).AlterColumn(nameof(AkeneoAssetMapping.AkeneoFamilyCode)).AsString(255).Nullable();
        Alter.Table(table).AlterColumn(nameof(AkeneoAssetMapping.SourceAttributeCode)).AsString(255).NotNullable();
        Alter.Table(table)
            .AlterColumn(nameof(
                AkeneoAssetMapping.FallbackSourceAttributeCode))
            .AsString(255)
            .Nullable();
        Alter.Table(table).AlterColumn(nameof(AkeneoAssetMapping.AssetFamilyCode)).AsString(255).Nullable();
        Alter.Table(table).AlterColumn(nameof(AkeneoAssetMapping.AssetMediaAttributeCode)).AsString(255).Nullable();
        Alter.Table(table).AlterColumn(nameof(AkeneoAssetMapping.AssetMediaType)).AsString(100).Nullable();
        Alter.Table(table).AlterColumn(nameof(AkeneoAssetMapping.RoleAttributeCode)).AsString(255).Nullable();
        Alter.Table(table).AlterColumn(nameof(AkeneoAssetMapping.RoleValuesCsv)).AsString(1000).Nullable();
        Alter.Table(table).AlterColumn(nameof(AkeneoAssetMapping.SortOrderAttributeCode)).AsString(255).Nullable();
        Alter.Table(table).AlterColumn(nameof(AkeneoAssetMapping.AltTextTemplate)).AsString(2000).Nullable();
        Alter.Table(table).AlterColumn(nameof(AkeneoAssetMapping.TitleTextTemplate)).AsString(2000).Nullable();
        Alter.Table(table).AlterColumn(nameof(AkeneoAssetMapping.SeoFilenameTemplate)).AsString(1000).Nullable();
        Alter.Table(table).AlterColumn(nameof(AkeneoAssetMapping.CustomPropertyKey)).AsString(255).Nullable();

        var indexName = $"IX_{table}_FamilyKey";
        if (!IndexExists(table, indexName))
        {
            Create.Index(indexName)
                .OnTable(table)
                .OnColumn(nameof(AkeneoAssetMapping.AkeneoFamilyCode)).Ascending()
                .OnColumn(nameof(AkeneoAssetMapping.MappingKey)).Ascending()
                .WithOptions().Unique();
        }
    }

    private void EnsureManagedAssetSchema()
    {
        var table = GetTableName<AkeneoManagedAsset>();

        Alter.Table(table).AlterColumn(nameof(AkeneoManagedAsset.AssetMappingKey)).AsString(100).NotNullable();
        Alter.Table(table).AlterColumn(nameof(AkeneoManagedAsset.SourceIdentityHash)).AsString(64).NotNullable();
        Alter.Table(table).AlterColumn(nameof(AkeneoManagedAsset.SourceFingerprint)).AsString(64).Nullable();
        Alter.Table(table).AlterColumn(nameof(AkeneoManagedAsset.SourceAttributeCode)).AsString(255).Nullable();
        Alter.Table(table).AlterColumn(nameof(AkeneoManagedAsset.AssetFamilyCode)).AsString(255).Nullable();
        Alter.Table(table).AlterColumn(nameof(AkeneoManagedAsset.AssetCode)).AsString(255).Nullable();
        Alter.Table(table).AlterColumn(nameof(AkeneoManagedAsset.MediaFileCode)).AsString(500).Nullable();
        Alter.Table(table).AlterColumn(nameof(AkeneoManagedAsset.SourceUrl)).AsString(2000).Nullable();
        Alter.Table(table).AlterColumn(nameof(AkeneoManagedAsset.DestinationKey)).AsString(255).Nullable();

        var identityIndex = $"IX_{table}_ProductMappingIdentityV2";
        if (!IndexExists(table, identityIndex))
        {
            Create.Index(identityIndex)
                .OnTable(table)
                .OnColumn(nameof(AkeneoManagedAsset.NopProductId)).Ascending()
                .OnColumn(nameof(AkeneoManagedAsset.AssetMappingKey)).Ascending()
                .OnColumn(nameof(AkeneoManagedAsset.SourceIdentityHash)).Ascending()
                .WithOptions().Unique();
        }

        var destinationIndex = $"IX_{table}_Destination";
        if (!IndexExists(table, destinationIndex))
        {
            Create.Index(destinationIndex)
                .OnTable(table)
                .OnColumn(nameof(AkeneoManagedAsset.NopProductId)).Ascending()
                .OnColumn(nameof(AkeneoManagedAsset.DestinationTypeId)).Ascending();
        }
    }

    #endregion

    #region Sync profiles

    private void EnsureSyncProfileColumns()
    {
        var table = GetTableName<AkeneoSyncProfile>();

        if (!ColumnExists(
                table,
                nameof(
                    AkeneoSyncProfile.MissingProductBehaviorId)))
        {
            Create.Column(
                    nameof(
                        AkeneoSyncProfile.MissingProductBehaviorId))
                .OnTable(table)
                .AsInt32()
                .NotNullable()
                .WithDefaultValue(
                    (int)AkeneoMissingProductBehavior.Ignore);
        }

        if (!ColumnExists(table, nameof(AkeneoSyncProfile.AssetSyncModeId)))
        {
            Create.Column(nameof(AkeneoSyncProfile.AssetSyncModeId))
                .OnTable(table)
                .AsInt32()
                .NotNullable()
                .WithDefaultValue((int)AkeneoCollectionSyncMode.ReplaceManaged);
        }

        if (!ColumnExists(table, nameof(AkeneoSyncProfile.IncludeLinkedAssetUpdates)))
        {
            Create.Column(nameof(AkeneoSyncProfile.IncludeLinkedAssetUpdates))
                .OnTable(table)
                .AsBoolean()
                .NotNullable()
                .WithDefaultValue(false);
        }

    }

    #endregion

    #region Sync run records

    private void EnsureSyncRunRecordColumns()
    {
        var table = GetTableName<AkeneoSyncRunRecord>();

        if (!ColumnExists(
                table,
                nameof(AkeneoSyncRunRecord.SyncProfileId)))
        {
            Create.Column(nameof(AkeneoSyncRunRecord.SyncProfileId))
                .OnTable(table)
                .AsInt32()
                .Nullable();
        }

        if (!ColumnExists(
                table,
                nameof(AkeneoSyncRunRecord.RunModeId)))
        {
            Create.Column(nameof(AkeneoSyncRunRecord.RunModeId))
                .OnTable(table)
                .AsInt32()
                .NotNullable()
                .WithDefaultValue((int)AkeneoRunMode.Delta);
        }

        if (!ColumnExists(
                table,
                nameof(AkeneoSyncRunRecord.WatermarkUtc)))
        {
            Create.Column(nameof(AkeneoSyncRunRecord.WatermarkUtc))
                .OnTable(table)
                .AsDateTime2()
                .NotNullable()
                .WithDefault(SystemMethods.CurrentUTCDateTime);
        }

        if (!ColumnExists(
                table,
                nameof(AkeneoSyncRunRecord.WarningCount)))
        {
            Create.Column(nameof(AkeneoSyncRunRecord.WarningCount))
                .OnTable(table)
                .AsInt32()
                .NotNullable()
                .WithDefaultValue(0);
        }

        if (!ColumnExists(
                table,
                nameof(AkeneoSyncRunRecord.CompletedAllPages)))
        {
            Create.Column(
                    nameof(AkeneoSyncRunRecord.CompletedAllPages))
                .OnTable(table)
                .AsBoolean()
                .NotNullable()
                .WithDefaultValue(false);
        }

        if (!ColumnExists(
                table,
                nameof(AkeneoSyncRunRecord.WasTruncated)))
        {
            Create.Column(nameof(AkeneoSyncRunRecord.WasTruncated))
                .OnTable(table)
                .AsBoolean()
                .NotNullable()
                .WithDefaultValue(false);
        }

        if (!ColumnExists(
                table,
                nameof(
                    AkeneoSyncRunRecord
                        .ReconciliationCompleted)))
        {
            Create.Column(
                    nameof(
                        AkeneoSyncRunRecord
                            .ReconciliationCompleted))
                .OnTable(table)
                .AsBoolean()
                .NotNullable()
                .WithDefaultValue(false);
        }

        EnsureNullableStringColumn(
            table,
            nameof(AkeneoSyncRunRecord.ScopeHash),
            64);

        EnsureNullableStringColumn(
            table,
            nameof(AkeneoSyncRunRecord.SearchJsonSnapshot),
            4000);

        EnsureNullableStringColumn(
            table,
            nameof(AkeneoSyncRunRecord.ProfileSnapshotJson),
            4000);

        Alter.Table(table)
            .AlterColumn(nameof(AkeneoSyncRunRecord.ScopeHash))
            .AsString(64)
            .Nullable();
    }

    private void EnsureSyncRunRecordIndexes()
    {
        var table = GetTableName<AkeneoSyncRunRecord>();
        var indexName = $"IX_{table}_ProfileStatus";

        if (IndexExists(table, indexName))
            return;

        Create.Index(indexName)
            .OnTable(table)
            .OnColumn(nameof(AkeneoSyncRunRecord.SyncProfileId))
            .Ascending()
            .OnColumn(nameof(AkeneoSyncRunRecord.SyncStatusId))
            .Ascending()
            .OnColumn(nameof(AkeneoSyncRunRecord.StartedOnUtc))
            .Descending();
    }

    #endregion

    #region Product sync state

    private void EnsureProductSyncStateSchema()
    {
        var table = GetTableName<AkeneoProductSyncState>();

        Alter.Table(table)
            .AlterColumn(nameof(AkeneoProductSyncState.AkeneoCode))
            .AsString(255)
            .Nullable();

        Alter.Table(table)
            .AlterColumn(nameof(AkeneoProductSyncState.AkeneoUuid))
            .AsString(64)
            .Nullable();

        Alter.Table(table)
            .AlterColumn(
                nameof(AkeneoProductSyncState.AkeneoParentCode))
            .AsString(255)
            .Nullable();

        Alter.Table(table)
            .AlterColumn(
                nameof(
                    AkeneoProductSyncState.LastDesiredStateHash))
            .AsString(64)
            .Nullable();

        EnsureProductSyncStateProfileUuidIndex(table);
        EnsureProductSyncStateProfileCodeIndex(table);
        EnsureProductSyncStateLastSeenIndex(table);
    }

    private void EnsureProductSyncStateProfileUuidIndex(
        string table)
    {
        var indexName = $"IX_{table}_ProfileUuid";

        if (IndexExists(table, indexName))
            return;

        Create.Index(indexName)
            .OnTable(table)
            .OnColumn(
                nameof(AkeneoProductSyncState.SyncProfileId))
            .Ascending()
            .OnColumn(
                nameof(
                    AkeneoProductSyncState.AkeneoEntityTypeId))
            .Ascending()
            .OnColumn(nameof(AkeneoProductSyncState.AkeneoUuid))
            .Ascending();
    }

    private void EnsureProductSyncStateProfileCodeIndex(
        string table)
    {
        var indexName = $"IX_{table}_ProfileCode";

        if (IndexExists(table, indexName))
            return;

        Create.Index(indexName)
            .OnTable(table)
            .OnColumn(
                nameof(AkeneoProductSyncState.SyncProfileId))
            .Ascending()
            .OnColumn(
                nameof(
                    AkeneoProductSyncState.AkeneoEntityTypeId))
            .Ascending()
            .OnColumn(nameof(AkeneoProductSyncState.AkeneoCode))
            .Ascending();
    }

    private void EnsureProductSyncStateLastSeenIndex(
        string table)
    {
        var indexName = $"IX_{table}_LastSeen";

        if (IndexExists(table, indexName))
            return;

        Create.Index(indexName)
            .OnTable(table)
            .OnColumn(
                nameof(AkeneoProductSyncState.SyncProfileId))
            .Ascending()
            .OnColumn(
                nameof(
                    AkeneoProductSyncState.LastSeenRunRecordId))
            .Ascending();
    }

    #endregion

    #region Managed relations

    private void EnsureManagedRelationSchema()
    {
        var table = GetTableName<AkeneoManagedRelation>();

        Alter.Table(table)
            .AlterColumn(
                nameof(
                    AkeneoManagedRelation.AkeneoAttributeCode))
            .AsString(255)
            .Nullable();

        Alter.Table(table)
            .AlterColumn(
                nameof(AkeneoManagedRelation.AkeneoValueCode))
            .AsString(255)
            .Nullable();

        EnsureManagedRelationProductTypeIndex(table);
        EnsureManagedRelationEntityIndex(table);
    }

    private void EnsureManagedRelationProductTypeIndex(
        string table)
    {
        var indexName = $"IX_{table}_ProductType";

        if (IndexExists(table, indexName))
            return;

        Create.Index(indexName)
            .OnTable(table)
            .OnColumn(
                nameof(AkeneoManagedRelation.SyncProfileId))
            .Ascending()
            .OnColumn(
                nameof(AkeneoManagedRelation.NopProductId))
            .Ascending()
            .OnColumn(
                nameof(AkeneoManagedRelation.RelationTypeId))
            .Ascending();
    }

    private void EnsureManagedRelationEntityIndex(
        string table)
    {
        var indexName = $"IX_{table}_RelationEntity";

        if (IndexExists(table, indexName))
            return;

        Create.Index(indexName)
            .OnTable(table)
            .OnColumn(
                nameof(AkeneoManagedRelation.SyncProfileId))
            .Ascending()
            .OnColumn(
                nameof(AkeneoManagedRelation.RelationTypeId))
            .Ascending()
            .OnColumn(
                nameof(
                    AkeneoManagedRelation.NopRelationEntityId))
            .Ascending()
            .WithOptions()
            .Unique();
    }

    #endregion

    #region Sync leases

    private void EnsureSyncLeaseSchema()
    {
        var table = GetTableName<AkeneoSyncLease>();

        Alter.Table(table)
            .AlterColumn(nameof(AkeneoSyncLease.LockKey))
            .AsString(200)
            .NotNullable();

        var indexName = $"IX_{table}_LockKey";

        if (IndexExists(table, indexName))
            return;

        Create.Index(indexName)
            .OnTable(table)
            .OnColumn(nameof(AkeneoSyncLease.LockKey))
            .Ascending()
            .WithOptions()
            .Unique();
    }

    #endregion

    #region Helpers

    private static string GetTableName<T>()
    {
        return NameCompatibilityManager.GetTableName(typeof(T));
    }

    private bool ColumnExists(
        string table,
        string column)
    {
        return Schema.Table(table)
            .Column(column)
            .Exists();
    }

    private bool IndexExists(
        string table,
        string index)
    {
        return Schema.Table(table)
            .Index(index)
            .Exists();
    }

    private void EnsureNullableStringColumn(
        string table,
        string column,
        int length)
    {
        if (ColumnExists(table, column))
            return;

        Create.Column(column)
            .OnTable(table)
            .AsString(length)
            .Nullable();
    }

    #endregion
}