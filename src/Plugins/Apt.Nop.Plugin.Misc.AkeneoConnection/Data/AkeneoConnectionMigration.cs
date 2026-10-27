using System.Data;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using FluentMigrator;
using Nop.Core;
using Nop.Data;
using Nop.Data.Extensions;
using Nop.Data.Mapping;
using Nop.Data.Migrations;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Data;

// Every step only adds what is missing, so the version moves forward whenever
// a step is added: a database that already ran an earlier version runs this one
// again and picks up only the new step. Column changes on an indexed column run
// only while its index is missing, since SQL Server rejects altering an indexed
// column on such a rerun.
[NopMigration(
    "2026-10-27 22:00:00",
    "AkeneoConnection: Consolidated schema (mappings, profile rules, family availability, assets, sync state, category content, sub-model parents, new-product defaults, combination availability source)",
    MigrationProcessType.NoMatter)]
public sealed class AkeneoConnectionMigration : Migration
{
    // FluentMigrator queues table creation until Up has finished. Treat columns
    // supplied by TableFor as present while building this migration's expressions.
    private readonly HashSet<string> _createdTables = new(StringComparer.OrdinalIgnoreCase);

    public override void Up()
    {
        if (!DataSettingsManager.IsDatabaseInstalled())
            return;

        _createdTables.Clear();
        EnsureTables();

        EnsureAttributeMappingColumns();
        EnsureAttributeMappingFallbackSourceSchema();
        EnsureFamilyMappingColumns();
        EnsureSubModelRuleColumns();
        EnsureAssetMappingSchema();
        EnsureManagedAssetSchema();
        EnsureSyncProfileColumns();
        EnsureAdditionalSyncProfileColumns();
        EnsureFamilyCombinationAvailabilitySchema();
        EnsureSyncRunRecordColumns();
        DropRawPayloadSnapshotColumns();

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
        EnsureTable<AkeneoCategoryContentMapping>();
        EnsureTable<AkeneoCategoryContentState>();
    }

    private void EnsureTable<T>() where T : BaseEntity
    {
        var table = GetTableName<T>();

        if (!Schema.Table(table).Exists())
        {
            Create.TableFor<T>();
            _createdTables.Add(table);
        }
    }

    #endregion

    #region Attribute mappings

    private void EnsureAttributeMappingColumns()
    {
        var table = GetTableName<AkeneoAttributeMapping>();

        EnsureNullableStringColumn(table, nameof(AkeneoAttributeMapping.ApplyWhen), 4000);

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
                nameof(AkeneoFamilyMapping.AkeneoFamilyVariantCode)))
        {
            Create.Column(nameof(AkeneoFamilyMapping.AkeneoFamilyVariantCode))
                .OnTable(table)
                .AsString(100)
                .NotNullable()
                .WithDefaultValue(string.Empty);
        }

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

        // New-product defaults: nullable IDs keep the nopCommerce default.
        foreach (var column in new[]
                 {
                     nameof(AkeneoFamilyMapping.NewProductTaxCategoryId),
                     nameof(AkeneoFamilyMapping.NewProductWarehouseId),
                     nameof(AkeneoFamilyMapping.NewProductTemplateId)
                 })
        {
            if (!ColumnExists(table, column))
                Create.Column(column).OnTable(table).AsInt32().Nullable();
        }

        if (!ColumnExists(table, nameof(AkeneoFamilyMapping.NewProductIsShipEnabled)))
        {
            Create.Column(nameof(AkeneoFamilyMapping.NewProductIsShipEnabled))
                .OnTable(table)
                .AsBoolean()
                .NotNullable()
                .WithDefaultValue(false);
        }

        // Existing managed families keep Akeneo enabled status as their availability source.
        if (!ColumnExists(table, nameof(AkeneoFamilyMapping.CombinationAvailabilitySourceId)))
        {
            Create.Column(nameof(AkeneoFamilyMapping.CombinationAvailabilitySourceId))
                .OnTable(table)
                .AsInt32()
                .NotNullable()
                .WithDefaultValue((int)AkeneoCombinationAvailabilitySource.EnabledStatus);
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
        var legacyIndexName = $"IX_{table}_AkeneoFamilyCode";
        var indexName = $"IX_{table}_FamilyVariantScope";

        // Drop the legacy family-code-only unique index first; it would
        // otherwise reject a second mapping row for the same family.
        if (IndexExists(table, legacyIndexName))
            Delete.Index(legacyIndexName).OnTable(table);

        if (IndexExists(table, indexName))
            return;

        Create.Index(indexName)
            .OnTable(table)
            .OnColumn(nameof(AkeneoFamilyMapping.AkeneoFamilyCode))
            .Ascending()
            .OnColumn(nameof(AkeneoFamilyMapping.AkeneoFamilyVariantCode))
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

        if (!IndexExists(table, $"IX_{table}_TypeCode"))
        {
            Alter.Table(table)
                .AlterColumn(nameof(AkeneoNopEntityMapping.AkeneoCode))
                .AsString(400)
                .Nullable();

            Alter.Table(table)
                .AlterColumn(nameof(AkeneoNopEntityMapping.AkeneoUuid))
                .AsString(64)
                .Nullable();
        }

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

        // Databases whose asset mapping table predates this column need it added
        // before the AlterColumn calls below can touch it.
        EnsureNullableStringColumn(
            table,
            nameof(AkeneoAssetMapping.FallbackSourceAttributeCode),
            255);

        var indexName = $"IX_{table}_FamilyKey";
        if (!IndexExists(table, indexName))
        {
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

        var identityIndex = $"IX_{table}_ProductMappingIdentityV2";
        if (!IndexExists(table, identityIndex))
        {
            Alter.Table(table).AlterColumn(nameof(AkeneoManagedAsset.AssetMappingKey)).AsString(100).NotNullable();
            Alter.Table(table).AlterColumn(nameof(AkeneoManagedAsset.SourceIdentityHash)).AsString(64).NotNullable();
            Alter.Table(table).AlterColumn(nameof(AkeneoManagedAsset.SourceFingerprint)).AsString(64).Nullable();
            Alter.Table(table).AlterColumn(nameof(AkeneoManagedAsset.SourceAttributeCode)).AsString(255).Nullable();
            Alter.Table(table).AlterColumn(nameof(AkeneoManagedAsset.AssetFamilyCode)).AsString(255).Nullable();
            Alter.Table(table).AlterColumn(nameof(AkeneoManagedAsset.AssetCode)).AsString(255).Nullable();
            Alter.Table(table).AlterColumn(nameof(AkeneoManagedAsset.MediaFileCode)).AsString(500).Nullable();
            Alter.Table(table).AlterColumn(nameof(AkeneoManagedAsset.SourceUrl)).AsString(2000).Nullable();
            Alter.Table(table).AlterColumn(nameof(AkeneoManagedAsset.DestinationKey)).AsString(255).Nullable();

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

        if (!ColumnExists(
                table,
                nameof(AkeneoSyncProfile.CompletenessFilterId)))
        {
            Create.Column(nameof(AkeneoSyncProfile.CompletenessFilterId))
                .OnTable(table)
                .AsInt32()
                .NotNullable()
                .WithDefaultValue((int)AkeneoCompletenessFilter.None);
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

        if (!IndexExists(table, $"IX_{table}_ProfileCode"))
        {
            Alter.Table(table)
                .AlterColumn(nameof(AkeneoProductSyncState.AkeneoCode))
                .AsString(255)
                .Nullable();

            Alter.Table(table)
                .AlterColumn(nameof(AkeneoProductSyncState.AkeneoUuid))
                .AsString(64)
                .Nullable();
        }

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
        var indexName = $"IX_{table}_LockKey";

        if (IndexExists(table, indexName))
            return;

        Alter.Table(table)
            .AlterColumn(nameof(AkeneoSyncLease.LockKey))
            .AsString(200)
            .NotNullable();

        Create.Index(indexName)
            .OnTable(table)
            .OnColumn(nameof(AkeneoSyncLease.LockKey))
            .Ascending()
            .WithOptions()
            .Unique();
    }

    #endregion

    #region Consolidated profile settings and family availability

    private void EnsureAdditionalSyncProfileColumns()
    {
        var table = GetTableName<AkeneoSyncProfile>();
        EnsureNullableStringColumn(table, nameof(AkeneoSyncProfile.SkipRulesJson), int.MaxValue);
        if (!ColumnExists(table, nameof(AkeneoSyncProfile.SyncLogRetentionEnabled)))
        {
            Create.Column(nameof(AkeneoSyncProfile.SyncLogRetentionEnabled))
                .OnTable(table)
                .AsBoolean()
                .NotNullable()
                .WithDefaultValue(false);
        }

        if (!ColumnExists(table, nameof(AkeneoSyncProfile.SyncItemLogRetentionDays)))
        {
            Create.Column(nameof(AkeneoSyncProfile.SyncItemLogRetentionDays))
                .OnTable(table)
                .AsInt32()
                .NotNullable()
                .WithDefaultValue(30);
        }

        if (!ColumnExists(table, nameof(AkeneoSyncProfile.SyncRunRecordRetentionDays)))
        {
            Create.Column(nameof(AkeneoSyncProfile.SyncRunRecordRetentionDays))
                .OnTable(table)
                .AsInt32()
                .NotNullable()
                .WithDefaultValue(90);
        }

        if (!ColumnExists(table, nameof(AkeneoSyncProfile.TagSyncModeId)))
        {
            Create.Column(nameof(AkeneoSyncProfile.TagSyncModeId))
                .OnTable(table)
                .AsInt32()
                .NotNullable()
                .WithDefaultValue((int)AkeneoCollectionSyncMode.Disabled);
        }

        if (!ColumnExists(table, nameof(AkeneoSyncProfile.RelatedProductSyncModeId)))
        {
            Create.Column(nameof(AkeneoSyncProfile.RelatedProductSyncModeId))
                .OnTable(table)
                .AsInt32()
                .NotNullable()
                .WithDefaultValue((int)AkeneoCollectionSyncMode.Disabled);
        }

        if (!ColumnExists(table, nameof(AkeneoSyncProfile.RelatedProductAssociationType)))
        {
            Create.Column(nameof(AkeneoSyncProfile.RelatedProductAssociationType))
                .OnTable(table)
                .AsString(100)
                .Nullable();
        }

        // Cross-sell sync, off by default.
        if (!ColumnExists(table, nameof(AkeneoSyncProfile.CrossSellSyncModeId)))
        {
            Create.Column(nameof(AkeneoSyncProfile.CrossSellSyncModeId))
                .OnTable(table)
                .AsInt32()
                .NotNullable()
                .WithDefaultValue((int)AkeneoCollectionSyncMode.Disabled);
        }

        if (!ColumnExists(table, nameof(AkeneoSyncProfile.CrossSellAssociationType)))
        {
            Create.Column(nameof(AkeneoSyncProfile.CrossSellAssociationType))
                .OnTable(table)
                .AsString(100)
                .Nullable();
        }
    }

    /// <summary>
    /// Sub-model parent rule settings. Existing rules keep the family's parent level.
    /// </summary>
    private void EnsureSubModelRuleColumns()
    {
        var table = GetTableName<AkeneoFamilySubModelRule>();

        if (!ColumnExists(table, nameof(AkeneoFamilySubModelRule.ParentLevelId)))
        {
            Create.Column(nameof(AkeneoFamilySubModelRule.ParentLevelId))
                .OnTable(table)
                .AsInt32()
                .NotNullable()
                .WithDefaultValue((int)AkeneoSubModelParentLevel.FamilySetting);
        }

        if (!ColumnExists(table, nameof(AkeneoFamilySubModelRule.AssociatedProductAttributeId)))
        {
            Create.Column(nameof(AkeneoFamilySubModelRule.AssociatedProductAttributeId))
                .OnTable(table)
                .AsInt32()
                .Nullable();
        }

        EnsureNullableStringColumn(table, nameof(AkeneoFamilySubModelRule.AssociatedValueNameTemplate), int.MaxValue);

        if (!ColumnExists(table, nameof(AkeneoFamilySubModelRule.HideChildProducts)))
        {
            Create.Column(nameof(AkeneoFamilySubModelRule.HideChildProducts))
                .OnTable(table)
                .AsBoolean()
                .NotNullable()
                .WithDefaultValue(true);
        }
    }

    /// <summary>
    /// Item logs stored the raw Akeneo payload, but nothing displayed it, and
    /// the profile toggle that enabled it is gone.
    /// </summary>
    private void DropRawPayloadSnapshotColumns()
    {
        DropColumnIfExists(GetTableName<AkeneoSyncItemLog>(), "RawPayloadSnapshot");
        DropColumnIfExists(GetTableName<AkeneoSyncProfile>(), "SaveRawPayloadSnapshot");
    }

    private void EnsureFamilyCombinationAvailabilitySchema()
    {
        var familyTable = GetTableName<AkeneoFamilyMapping>();
        var profileTable = GetTableName<AkeneoSyncProfile>();
        const string column = nameof(AkeneoFamilyMapping.ManageVariantsByEnabledStatus);
        const string legacyColumn = "CombinationAvailabilityCondition";
        // Transfer once, when adding the toggle. Never restore a setting later switched off.
        if (ColumnExists(familyTable, column)) return;
        Create.Column(column).OnTable(familyTable).AsBoolean().NotNullable().WithDefaultValue(false);

        var hasFamilyCondition = Schema.Table(familyTable).Column(legacyColumn).Exists();
        var hasProfileCondition = Schema.Table(profileTable).Column(legacyColumn).Exists();
        if (!hasFamilyCondition && !hasProfileCondition) return;
        Execute.WithConnection((connection, transaction) =>
        {
            string Quote(string name) => connection.GetType().Name.Contains("MySql", StringComparison.OrdinalIgnoreCase)
                ? $"`{name}`" : connection.GetType().Name.Contains("Npgsql", StringComparison.OrdinalIgnoreCase)
                    ? $"\"{name}\"" : $"[{name}]";
            using var read = connection.CreateCommand();
            read.Transaction = transaction;
            var profiles = new List<(string FamilyCodes, string Condition)>();
            if (!hasFamilyCondition && hasProfileCondition)
            {
                read.CommandText = $"SELECT {Quote("AkeneoFamilyCodes")}, {Quote(legacyColumn)} FROM {Quote(profileTable)}";
                using var reader = read.ExecuteReader();
                while (reader.Read()) profiles.Add((reader.IsDBNull(0) ? null : reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1)));
            }
            var legacyValue = hasFamilyCondition ? Quote(legacyColumn) : "NULL";
            read.CommandText = $"SELECT {Quote("Id")}, {Quote("AkeneoFamilyCode")}, {legacyValue} FROM {Quote(familyTable)} WHERE {Quote("VariantRelationshipModeId")} = {(int)AkeneoVariantRelationshipMode.ProductAttributeCombinations}";
            var families = new List<(int Id, string Code, string Condition)>();
            using (var reader = read.ExecuteReader())
                while (reader.Read()) families.Add((Convert.ToInt32(reader.GetValue(0)), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
            foreach (var family in families)
            {
                var condition = hasFamilyCondition ? family.Condition : SelectCondition(family.Code, profiles);
                if (!IsLegacyEnabledStatusCondition(condition)) continue;
                using var update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = $"UPDATE {Quote(familyTable)} SET {Quote(column)} = @enabled WHERE {Quote("Id")} = @id";
                var enabled = update.CreateParameter(); enabled.ParameterName = "@enabled"; enabled.DbType = DbType.Boolean; enabled.Value = true;
                var id = update.CreateParameter(); id.ParameterName = "@id"; id.DbType = DbType.Int32; id.Value = family.Id;
                update.Parameters.Add(enabled); update.Parameters.Add(id); update.ExecuteNonQuery();
            }
        });
    }

    public static bool IsLegacyEnabledStatusCondition(string condition)
    {
        var normalized = new string((condition ?? string.Empty).Where(c => !char.IsWhiteSpace(c)).ToArray())
            .Trim('{', '}', '(', ')').Replace("\"", string.Empty).Replace("'", string.Empty);
        return normalized.Equals("enabled==true", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("enabled!=false", StringComparison.OrdinalIgnoreCase);
    }

    public static string SelectCondition(string familyCode, IEnumerable<(string FamilyCodes, string Condition)> profiles)
    {
        var conditions = profiles.Where(p => string.IsNullOrWhiteSpace(p.FamilyCodes) ||
                p.FamilyCodes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(familyCode, StringComparer.OrdinalIgnoreCase))
            .Select(p => p.Condition?.Trim() ?? string.Empty).Distinct(StringComparer.Ordinal).ToList();
        return conditions.Count == 1 && !string.IsNullOrWhiteSpace(conditions[0]) ? conditions[0] : null;
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
        return _createdTables.Contains(table) || Schema.Table(table)
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

    // A table created by this migration never has the column.
    private void DropColumnIfExists(string table, string column)
    {
        if (!_createdTables.Contains(table) &&
            Schema.Table(table).Exists() &&
            Schema.Table(table).Column(column).Exists())
        {
            Delete.Column(column).FromTable(table);
        }
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
