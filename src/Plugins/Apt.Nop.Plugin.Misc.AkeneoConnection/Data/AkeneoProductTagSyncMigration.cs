using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using FluentMigrator;
using Nop.Data;
using Nop.Data.Mapping;
using Nop.Data.Migrations;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Data;

/// <summary>
/// Adds the per-profile product tag sync mode, off (Disabled) for existing
/// profiles. New installs already get the column from the entity.
/// </summary>
[NopMigration(
    "2026-10-20 01:00:00",
    "AkeneoConnection: Sync profile product tag sync mode",
    MigrationProcessType.NoMatter)]
public sealed class AkeneoProductTagSyncMigration : Migration
{
    public override void Up()
    {
        if (!DataSettingsManager.IsDatabaseInstalled())
            return;

        var table = NameCompatibilityManager.GetTableName(typeof(AkeneoSyncProfile));

        if (!Schema.Table(table).Column(nameof(AkeneoSyncProfile.TagSyncModeId)).Exists())
        {
            Create.Column(nameof(AkeneoSyncProfile.TagSyncModeId))
                .OnTable(table)
                .AsInt32()
                .NotNullable()
                .WithDefaultValue((int)AkeneoCollectionSyncMode.Disabled);
        }
    }

    public override void Down()
    {
        // Intentionally preserve sync profile configuration.
    }
}
