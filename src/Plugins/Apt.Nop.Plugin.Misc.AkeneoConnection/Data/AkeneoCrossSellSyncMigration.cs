using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using FluentMigrator;
using Nop.Data;
using Nop.Data.Mapping;
using Nop.Data.Migrations;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Data;

/// <summary>
/// Adds the sync profile's cross-sell settings, off by default. A fresh install creates them with
/// the table, so each column is added only when missing.
/// </summary>
[NopMigration(
    "2026-10-25 00:00:00",
    "AkeneoConnection: Add cross-sell sync profile columns",
    MigrationProcessType.NoMatter)]
public sealed class AkeneoCrossSellSyncMigration : Migration
{
    public override void Up()
    {
        if (!DataSettingsManager.IsDatabaseInstalled())
            return;

        var table = NameCompatibilityManager.GetTableName(typeof(AkeneoSyncProfile));
        if (!Schema.Table(table).Exists())
            return;

        if (!Schema.Table(table).Column(nameof(AkeneoSyncProfile.CrossSellSyncModeId)).Exists())
        {
            Create.Column(nameof(AkeneoSyncProfile.CrossSellSyncModeId))
                .OnTable(table)
                .AsInt32()
                .NotNullable()
                .WithDefaultValue((int)AkeneoCollectionSyncMode.Disabled);
        }

        if (!Schema.Table(table).Column(nameof(AkeneoSyncProfile.CrossSellAssociationType)).Exists())
        {
            Create.Column(nameof(AkeneoSyncProfile.CrossSellAssociationType))
                .OnTable(table)
                .AsString(100)
                .Nullable();
        }
    }

    public override void Down()
    {
        // Leaving the columns is harmless; nothing reads them once the plugin is removed.
    }
}
