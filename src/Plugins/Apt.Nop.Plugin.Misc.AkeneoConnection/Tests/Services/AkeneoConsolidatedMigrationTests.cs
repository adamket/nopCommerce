using System.Reflection;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Data;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using FluentMigrator;
using FluentMigrator.Runner;
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
        => PlanMigration(true, tablesExist, missingColumns);

    private static IList<IMigrationExpression> PlanMigration(bool availabilityAlreadyApplied, bool tablesExist, params string[] missingColumns)
    {
        var installed = typeof(DataSettingsManager).GetField("_databaseIsInstalled", BindingFlags.Static | BindingFlags.NonPublic)!;
        var initialized = typeof(NameCompatibilityManager).GetField("_isInitialized", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previousInstalled = installed.GetValue(null);
        var previousInitialized = initialized.GetValue(null);
        var previousEngine = Singleton<IEngine>.Instance;
        var previousFinder = Singleton<ITypeFinder>.Instance;
        var previousData = Singleton<DataConfig>.Instance;
        try
        {
            installed.SetValue(null, true);
            Singleton<DataConfig>.Instance = new DataConfig { DataProvider = DataProviderType.MySql };
            var finder = new Mock<ITypeFinder>();
            finder.Setup(x => x.FindClassesOfType(typeof(global::Nop.Data.Mapping.Builders.IEntityBuilder), true))
                .Returns(new[] { typeof(AkeneoSyncProfileBuilder) });
            Singleton<ITypeFinder>.Instance = finder.Object;
            var engine = new Mock<IEngine>();
            engine.Setup(x => x.ResolveUnregistered(typeof(AkeneoSyncProfileBuilder))).Returns(new AkeneoSyncProfileBuilder());
            Singleton<IEngine>.Instance = engine.Object;
            var schema = new Mock<IQuerySchema>();
            schema.Setup(x => x.TableExists(It.IsAny<string>(), It.IsAny<string>())).Returns(tablesExist);
            schema.Setup(x => x.ColumnExists(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns((string _, string table, string column) => tablesExist && !missingColumns.Contains(column));
            schema.Setup(x => x.IndexExists(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns((string _, string table, string index) => tablesExist && !index.EndsWith("_AkeneoFamilyCode"));
            var versions = new Mock<IVersionLoader>();
            versions.Setup(loader => loader.VersionInfo.HasAppliedMigration(new DateTime(2026, 10, 30, 13, 0, 0).Ticks)).Returns(availabilityAlreadyApplied);
            var context = new MigrationContext(schema.Object, Mock.Of<IServiceProvider>(), "");
            new AkeneoConnectionMigration(versions.Object).GetUpExpressions(context);
            return context.Expressions.ToList();
        }
        finally
        {
            installed.SetValue(null, previousInstalled);
            initialized.SetValue(null, previousInitialized);
            Singleton<IEngine>.Instance = previousEngine;
            Singleton<ITypeFinder>.Instance = previousFinder;
            Singleton<DataConfig>.Instance = previousData;
        }
    }

    // Dropped by the migration; an up-to-date schema no longer has them.
    private static readonly string[] DroppedColumns = ["RawPayloadSnapshot", "SaveRawPayloadSnapshot"];

    [Test]
    public void Axis_option_name_upgrade_adds_a_nullable_column_once()
    {
        var expressions = Plan(true, nameof(AkeneoFamilyVariantAxisMapping.OptionNameTemplate));
        var column = expressions.OfType<CreateColumnExpression>().Single();
        Assert.That(column.TableName, Does.EndWith(nameof(AkeneoFamilyVariantAxisMapping)));
        Assert.That(column.Column.Name, Is.EqualTo(nameof(AkeneoFamilyVariantAxisMapping.OptionNameTemplate)));
        Assert.That(column.Column.IsNullable, Is.True);
        Assert.That(Plan(true).OfType<CreateColumnExpression>(), Is.Empty);
    }

    [Test]
    public void Standalone_name_upgrade_adds_a_nullable_column_once()
    {
        var expressions = Plan(true, "StandaloneProductNameTemplate");
        var column = expressions.OfType<CreateColumnExpression>().Single();
        Assert.That(column.TableName, Does.EndWith(nameof(AkeneoFamilySubModelRule)));
        Assert.That(column.Column.Name, Is.EqualTo("StandaloneProductNameTemplate"));
        Assert.That(column.Column.IsNullable, Is.True);
        Assert.That(Plan(true).OfType<CreateColumnExpression>(), Is.Empty);
    }

    [Test]
    public void Standalone_template_upgrade_adds_a_nullable_column_once()
    {
        var expressions = Plan(true, "StandaloneProductTemplateId");
        var column = expressions.OfType<CreateColumnExpression>().Single();
        Assert.That(column.TableName, Does.EndWith(nameof(AkeneoFamilySubModelRule)));
        Assert.That(column.Column.Name, Is.EqualTo("StandaloneProductTemplateId"));
        Assert.That(column.Column.IsNullable, Is.True);
        Assert.That(Plan(true).OfType<CreateColumnExpression>(), Is.Empty);
    }

    [Test]
    public void Single_consolidated_migration_adds_log_skips_off_on_install_and_upgrade()
    {
        var migrations = typeof(AkeneoConnectionMigration).Assembly.GetTypes()
            .Select(type => type.GetCustomAttribute<global::Nop.Data.Migrations.NopMigrationAttribute>())
            .Where(attribute => attribute != null).ToList();
        Assert.That(migrations, Has.Count.EqualTo(1));
        Assert.That(migrations.Single()!.Version, Is.GreaterThan(new DateTime(2026, 11, 1, 10, 0, 0).Ticks));
        var tables = Plan(false).OfType<CreateTableExpression>().ToList();
        var newProfile = tables.Single(table => table.TableName.EndsWith(nameof(AkeneoSyncProfile)));
        Assert.That(newProfile.Columns.Single(col => col.Name == nameof(AkeneoSyncProfile.LogSkips)).DefaultValue, Is.EqualTo(false));
        var upgrade = Plan(true, nameof(AkeneoSyncProfile.LogSkips));
        Assert.That(upgrade.OfType<CreateColumnExpression>().Single().Column.DefaultValue, Is.EqualTo(false));
        Assert.That(upgrade.OfType<UpdateDataExpression>(), Is.Empty, "Keep saved availability choices after the legacy upgrade.");
        Assert.That(tables.Any(table => table.TableName.EndsWith(nameof(AkeneoFamilyMapping))), Is.True);
        var rules = tables.Single(table => table.TableName.EndsWith(nameof(AkeneoFamilySubModelRule)));
        Assert.That(rules.Columns.Select(column => column.Name), Does.Contain("StandaloneProductTemplateId").And.Contain("StandaloneProductNameTemplate"));
        Assert.That(Plan(true, DroppedColumns).OfType<UpdateDataExpression>(), Is.Empty,
            "Schema refresh must not reapply the one-time availability selection.");
    }

    [Test]
    public void Published_upgrade_only_switches_managed_enabled_status_combinations()
    {
        var expressions = PlanMigration(false, true, nameof(AkeneoSyncProfile.LogSkips));
        var update = expressions.OfType<UpdateDataExpression>().Single();
        Assert.That(update.TableName, Does.EndWith(nameof(AkeneoFamilyMapping)));
        Assert.That(update.Set.ToDictionary(column => column.Key, column => column.Value), Is.EqualTo(new Dictionary<string, object>
        {
            [nameof(AkeneoFamilyMapping.CombinationAvailabilitySourceId)] = (int)AkeneoCombinationAvailabilitySource.PublishedRule
        }));
        Assert.That(update.Where.ToDictionary(column => column.Key, column => column.Value), Is.EqualTo(new Dictionary<string, object>
        {
            [nameof(AkeneoFamilyMapping.VariantRelationshipModeId)] = (int)AkeneoVariantRelationshipMode.ProductAttributeCombinations,
            [nameof(AkeneoFamilyMapping.ManageVariantsByEnabledStatus)] = true,
            [nameof(AkeneoFamilyMapping.CombinationAvailabilitySourceId)] = (int)AkeneoCombinationAvailabilitySource.EnabledStatus
        }));
    }

    [Test]
    public void New_combination_configuration_defaults_to_the_published_rule()
    {
        Assert.That(new AkeneoFamilyMapping { ManageVariantsByEnabledStatus = true }.CombinationAvailabilitySource,
            Is.EqualTo(AkeneoCombinationAvailabilitySource.PublishedRule));
        var editor = new Models.AkeneoFamilyMappingModel();
        Assert.That(editor.ManageVariantsByEnabledStatus, Is.True);
        Assert.That(editor.CombinationAvailabilitySourceId, Is.EqualTo((int)AkeneoCombinationAvailabilitySource.PublishedRule));
    }

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
            "NewProductTaxCategoryId", "NewProductIsShipEnabled", "NewProductAllowCustomerReviews", "NewProductManageInventoryMethodId", "NewProductWarehouseId", "NewProductTemplateId",
            "CombinationAvailabilitySourceId", "VariantDisplayOrderKeys", "StandaloneProductTemplateId", "StandaloneProductNameTemplate" };
        var created = Plan(true, columns.Concat(DroppedColumns).ToArray()).OfType<CreateColumnExpression>().ToList();
        Assert.That(created.Select(x => x.Column.Name), Is.EquivalentTo(columns));
        Assert.That(created.Single(x => x.Column.Name == "SyncItemLogRetentionDays").Column.DefaultValue, Is.EqualTo(30));
        Assert.That(created.Single(x => x.Column.Name == "SyncRunRecordRetentionDays").Column.DefaultValue, Is.EqualTo(90));
        Assert.That(created.Single(x => x.Column.Name == "TagSyncModeId").Column.DefaultValue, Is.EqualTo((int)AkeneoCollectionSyncMode.Disabled));
        Assert.That(created.Single(x => x.Column.Name == "CrossSellSyncModeId").Column.DefaultValue, Is.EqualTo((int)AkeneoCollectionSyncMode.Disabled));
        Assert.That(created.Single(x => x.Column.Name == "StandaloneProductTemplateId").Column.IsNullable, Is.True);
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
