using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using FluentMigrator;
using Nop.Data;
using Nop.Data.Mapping;
using Nop.Data.Migrations;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Data;

[NopMigration(
    "2026-07-23 16:00:00",
    "AkeneoConnection: Add asset mapping fallback source",
    MigrationProcessType.NoMatter)]
public sealed class AkeneoAssetMappingFallbackSourceMigration : MigrationBase
{
    public override void Up()
    {
        if (!DataSettingsManager.IsDatabaseInstalled())
            return;

        var table = NameCompatibilityManager.GetTableName(
            typeof(AkeneoAssetMapping));
        var column = nameof(
            AkeneoAssetMapping.FallbackSourceAttributeCode);

        if (!Schema.Table(table).Exists() ||
            Schema.Table(table).Column(column).Exists())
        {
            return;
        }

        Alter.Table(table)
            .AddColumn(column)
            .AsString(255)
            .Nullable();
    }

    public override void Down()
    {
        // Preserve asset mapping configuration during plugin downgrade/uninstall.
    }
}
