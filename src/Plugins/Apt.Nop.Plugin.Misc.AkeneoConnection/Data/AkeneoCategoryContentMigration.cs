using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using FluentMigrator;
using Nop.Data;
using Nop.Data.Extensions;
using Nop.Data.Mapping;
using Nop.Data.Migrations;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Data;

/// <summary>
/// Adds the category content mapping and per-category sync state tables.
/// Each table is created only when missing.
/// </summary>
[NopMigration(
    "2026-10-26 00:00:00",
    "AkeneoConnection: Add category content mapping and state tables",
    MigrationProcessType.NoMatter)]
public sealed class AkeneoCategoryContentMigration : Migration
{
    public override void Up()
    {
        if (!DataSettingsManager.IsDatabaseInstalled())
            return;

        if (!Schema.Table(NameCompatibilityManager.GetTableName(typeof(AkeneoCategoryContentMapping))).Exists())
            Create.TableFor<AkeneoCategoryContentMapping>();

        if (!Schema.Table(NameCompatibilityManager.GetTableName(typeof(AkeneoCategoryContentState))).Exists())
            Create.TableFor<AkeneoCategoryContentState>();
    }

    public override void Down()
    {
        // Preserve mappings and picture ownership if the plugin is downgraded.
    }
}
