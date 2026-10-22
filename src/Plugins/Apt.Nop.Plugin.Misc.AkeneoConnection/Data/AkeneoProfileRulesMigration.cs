using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using FluentMigrator;
using Nop.Data;
using Nop.Data.Mapping;
using Nop.Data.Migrations;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Data;

[NopMigration("2026-10-22 12:00:02", "AkeneoConnection: Profile skip rules", MigrationProcessType.NoMatter)]
public sealed class AkeneoProfileRulesMigration : Migration
{
    public override void Up()
    {
        if (!DataSettingsManager.IsDatabaseInstalled()) return;
        var table = NameCompatibilityManager.GetTableName(typeof(AkeneoSyncProfile));
        foreach (var column in new[] { nameof(AkeneoSyncProfile.SkipRulesJson) })
            if (!Schema.Table(table).Column(column).Exists())
                Create.Column(column).OnTable(table).AsString(int.MaxValue).Nullable();
    }

    public override void Down()
    {
        // Preserve configured profile rules.
    }
}
