using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using FluentMigrator;
using Nop.Data.Mapping;
using Nop.Data.Migrations;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Data;

[NopMigration(
    "2026-07-20 01:00:00",
    "AkeneoConnection: Add product model hierarchy mode",
    MigrationProcessType.Update)]
public class AkeneoProductModelHierarchyMigration : MigrationBase
{
    public override void Up()
    {
        var table = NameCompatibilityManager.GetTableName(
            typeof(AkeneoFamilyMapping));

        if (!Schema.Table(table).Exists() ||
            Schema.Table(table)
                .Column(nameof(AkeneoFamilyMapping.ProductModelHierarchyModeId))
                .Exists())
        {
            return;
        }

        Alter.Table(table)
            .AddColumn(nameof(AkeneoFamilyMapping.ProductModelHierarchyModeId))
            .AsInt32()
            .NotNullable()
            .WithDefaultValue(
                (int)AkeneoProductModelHierarchyMode.ImmediateParentProductModel);
    }

    public override void Down()
    {
        // Preserve family hierarchy configuration on downgrade.
    }
}
