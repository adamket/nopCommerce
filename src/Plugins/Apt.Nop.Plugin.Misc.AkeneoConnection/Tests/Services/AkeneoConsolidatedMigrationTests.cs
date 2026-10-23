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

    [Test]
    public void Existing_schema_rerun_does_not_create_or_delete_columns_or_repeat_legacy_transfer()
    {
        var expressions = Plan(true);
        Assert.That(expressions.Any(x => x is CreateColumnExpression or CreateTableExpression or DeleteColumnExpression or DeleteTableExpression), Is.False);
        Assert.That(expressions.Any(x => x.GetType().Name.Contains("DBOperation")), Is.False);
    }

    [Test]
    public void Older_schema_gets_every_consolidated_column_with_original_defaults()
    {
        var columns = new[] { "ApplyWhen", "SkipRulesJson", "CombinationAvailabilityCondition", "TagSyncModeId", "RelatedProductSyncModeId",
            "RelatedProductAssociationType", "SyncLogRetentionEnabled", "SyncItemLogRetentionDays", "SyncRunRecordRetentionDays" };
        var created = Plan(true, columns).OfType<CreateColumnExpression>().ToList();
        Assert.That(created.Select(x => x.Column.Name), Is.EquivalentTo(columns));
        Assert.That(created.Single(x => x.Column.Name == "SyncItemLogRetentionDays").Column.DefaultValue, Is.EqualTo(30));
        Assert.That(created.Single(x => x.Column.Name == "SyncRunRecordRetentionDays").Column.DefaultValue, Is.EqualTo(90));
        Assert.That(created.Single(x => x.Column.Name == "TagSyncModeId").Column.DefaultValue, Is.EqualTo((int)AkeneoCollectionSyncMode.Disabled));
    }

    [Test]
    public void Fresh_install_does_not_queue_duplicate_column_creation_after_table_creation()
    {
        var expressions = Plan(false);
        Assert.That(expressions.OfType<CreateTableExpression>().Count(), Is.GreaterThan(0));
        Assert.That(expressions.OfType<CreateColumnExpression>(), Is.Empty);
        Assert.That(expressions.OfType<CreateTableExpression>().SelectMany(x => x.Columns).Any(x => x.Name == "CombinationAvailabilityCondition"), Is.True);
    }
}
