using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using FluentMigrator;
using Nop.Data;
using Nop.Data.Mapping;
using Nop.Data.Migrations;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Data;

[NopMigration("2026-10-22 18:00:00", "AkeneoConnection: Computed mapping apply condition", MigrationProcessType.NoMatter)]
public sealed class AkeneoComputedMappingConditionMigration : Migration
{
    public override void Up()
    {
        if (!DataSettingsManager.IsDatabaseInstalled()) return;
        var table = NameCompatibilityManager.GetTableName(typeof(AkeneoAttributeMapping));
        if (!Schema.Table(table).Column(nameof(AkeneoAttributeMapping.ApplyWhen)).Exists())
            Create.Column(nameof(AkeneoAttributeMapping.ApplyWhen)).OnTable(table).AsString(4000).Nullable();
    }

    public override void Down()
    {
        // Preserve configured mapping conditions.
    }
}
