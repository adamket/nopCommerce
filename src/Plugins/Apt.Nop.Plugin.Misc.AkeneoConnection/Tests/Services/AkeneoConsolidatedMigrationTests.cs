using System.Reflection;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Data;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using FluentMigrator;
using FluentMigrator.Expressions;
using FluentMigrator.Infrastructure;
using Moq;
using Nop.Core.Infrastructure;
using Nop.Data;
using Nop.Data.Configuration;
using Nop.Data.Mapping;
using NUnit.Framework;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

[TestFixture, NonParallelizable]
public class AkeneoConsolidatedMigrationTests
{
    private static IList<IMigrationExpression> Plan(bool tablesExist, params string[] missingColumns)
    {
        var installed = typeof(DataSettingsManager).GetField("_databaseIsInstalled", BindingFlags.Static | BindingFlags.NonPublic)!;
        var initialized = typeof(NameCompatibilityManager).GetField("_isInitialized", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previousInstalled = installed.GetValue(null);
        var previousInitialized = initialized.GetValue(null);
        var previousFinder = Singleton<ITypeFinder>.Instance;
        var previousData = Singleton<DataConfig>.Instance;
        try
        {
            installed.SetValue(null, true);
            Singleton<DataConfig>.Instance = new DataConfig { DataProvider = DataProviderType.MySql };
            Singleton<ITypeFinder>.Instance = Mock.Of<ITypeFinder>();
            var schema = new Mock<IQuerySchema>();
            schema.Setup(x => x.TableExists(It.IsAny<string>(), It.IsAny<string>())).Returns(tablesExist);
            schema.Setup(x => x.ColumnExists(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns((string _, string table, string column) => tablesExist && !missingColumns.Contains(column));
            schema.Setup(x => x.IndexExists(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns((string _, string table, string index) => tablesExist && !index.EndsWith("_AkeneoFamilyCode"));
            var context = new MigrationContext(schema.Object, Mock.Of<IServiceProvider>(), "");
            new AkeneoConnectionMigration().GetUpExpressions(context);
            return context.Expressions.ToList();
        }
        finally
        {
            installed.SetValue(null, previousInstalled);
            initialized.SetValue(null, previousInitialized);
            Singleton<ITypeFinder>.Instance = previousFinder;
            Singleton<DataConfig>.Instance = previousData;
        }
    }

    // Dropped by the migration; an up-to-date schema no longer has them.
    private static readonly string[] DroppedColumns = ["RawPayloadSnapshot", "SaveRawPayloadSnapshot"];

    [Test]
    public void Existing_schema_rerun_does_not_create_or_delete_columns_or_repeat_legacy_transfer()
    {
        var expressions = Plan(true, DroppedColumns);
        Assert.That(expressions.Any(x => x is CreateColumnExpression or CreateTableExpression or DeleteColumnExpression or DeleteTableExpression), Is.False);
        Assert.That(expressions.Any(x => x.GetType().Name.Contains("DBOperation")), Is.False);
    }

    [Test]
    public void Existing_schema_rerun_does_not_alter_indexed_columns()
    {
        var indexedColumns = new[]
        {
            (Table: nameof(AkeneoNopEntityMapping), Column: nameof(AkeneoNopEntityMapping.AkeneoCode)),
            (Table: nameof(AkeneoNopEntityMapping), Column: nameof(AkeneoNopEntityMapping.AkeneoUuid)),
            (Table: nameof(AkeneoProductSyncState), Column: nameof(AkeneoProductSyncState.AkeneoCode)),
            (Table: nameof(AkeneoProductSyncState), Column: nameof(AkeneoProductSyncState.AkeneoUuid)),
            (Table: nameof(AkeneoAssetMapping), Column: nameof(AkeneoAssetMapping.MappingKey)),
            (Table: nameof(AkeneoAssetMapping), Column: nameof(AkeneoAssetMapping.AkeneoFamilyCode)),
            (Table: nameof(AkeneoManagedAsset), Column: nameof(AkeneoManagedAsset.AssetMappingKey)),
            (Table: nameof(AkeneoManagedAsset), Column: nameof(AkeneoManagedAsset.SourceIdentityHash)),
            (Table: nameof(AkeneoSyncLease), Column: nameof(AkeneoSyncLease.LockKey))
        };

        var altered = Plan(true, DroppedColumns).OfType<AlterColumnExpression>()
            .Select(x => (Table: x.TableName, Column: x.Column.Name))
            .ToList();

        Assert.That(altered.Where(x => indexedColumns.Any(i => x.Table.EndsWith(i.Table) && x.Column == i.Column)), Is.Empty);
    }

    [Test]
    public void Older_schema_gets_every_consolidated_column_with_original_defaults()
    {
        var columns = new[] { "ApplyWhen", "SkipRulesJson", "ManageVariantsByEnabledStatus", "TagSyncModeId", "RelatedProductSyncModeId",
            "RelatedProductAssociationType", "SyncLogRetentionEnabled", "SyncItemLogRetentionDays", "SyncRunRecordRetentionDays",
            "CrossSellSyncModeId", "CrossSellAssociationType",
            "ParentLevelId", "AssociatedProductAttributeId", "AssociatedValueNameTemplate", "HideChildProducts",
            "NewProductTaxCategoryId", "NewProductIsShipEnabled", "NewProductWarehouseId", "NewProductTemplateId",
            "CombinationAvailabilitySourceId", "VariantDisplayOrderKeys" };
        var created = Plan(true, columns.Concat(DroppedColumns).ToArray()).OfType<CreateColumnExpression>().ToList();
        Assert.That(created.Select(x => x.Column.Name), Is.EquivalentTo(columns));
        Assert.That(created.Single(x => x.Column.Name == "SyncItemLogRetentionDays").Column.DefaultValue, Is.EqualTo(30));
        Assert.That(created.Single(x => x.Column.Name == "SyncRunRecordRetentionDays").Column.DefaultValue, Is.EqualTo(90));
        Assert.That(created.Single(x => x.Column.Name == "TagSyncModeId").Column.DefaultValue, Is.EqualTo((int)AkeneoCollectionSyncMode.Disabled));
        Assert.That(created.Single(x => x.Column.Name == "CrossSellSyncModeId").Column.DefaultValue, Is.EqualTo((int)AkeneoCollectionSyncMode.Disabled));
        Assert.That(created.Single(x => x.Column.Name == "ParentLevelId").Column.DefaultValue, Is.EqualTo((int)AkeneoSubModelParentLevel.FamilySetting));
        Assert.That(created.Single(x => x.Column.Name == "HideChildProducts").Column.DefaultValue, Is.EqualTo(true));
    }

    [Test]
    public void A_schema_still_holding_the_raw_payload_columns_drops_them()
    {
        var dropped = Plan(true).OfType<DeleteColumnExpression>().SelectMany(x => x.ColumnNames).ToList();

        Assert.That(dropped, Is.EquivalentTo(DroppedColumns));
    }

    [Test]
    public void Fresh_install_does_not_queue_duplicate_column_creation_after_table_creation()
    {
        var expressions = Plan(false);
        Assert.That(expressions.OfType<CreateTableExpression>().Count(), Is.GreaterThan(0));
        Assert.That(expressions.OfType<CreateColumnExpression>(), Is.Empty);
        Assert.That(expressions.OfType<CreateTableExpression>().SelectMany(x => x.Columns).Any(x => x.Name == "ManageVariantsByEnabledStatus"), Is.True);
    }

    [TestCase("enabled == true", true)]
    [TestCase("{enabled != false}", true)]
    [TestCase("(Enabled == \"true\")", true)]
    [TestCase("enabled == false", false)]
    [TestCase("sync_status == published", false)]
    [TestCase(null, false)]
    [TestCase("", false)]
    public void Legacy_migration_only_enables_the_toggle_for_equivalent_enabled_status_rules(string? condition, bool expected)
    {
        Assert.That(AkeneoConnectionMigration.IsLegacyEnabledStatusCondition(condition!), Is.EqualTo(expected));
    }
}
