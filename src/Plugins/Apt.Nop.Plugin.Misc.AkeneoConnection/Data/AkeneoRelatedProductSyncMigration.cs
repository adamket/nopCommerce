using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using FluentMigrator;
using Nop.Data;
using Nop.Data.Mapping;
using Nop.Data.Migrations;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Data;

/// <summary>
/// Adds the per-profile related product sync settings, off (Disabled) for existing
/// profiles. New installs already get the columns from the entity.
/// </summary>
[NopMigration(
    "2026-10-20 02:00:00",
    "AkeneoConnection: Sync profile related product settings",
    MigrationProcessType.NoMatter)]
public sealed class AkeneoRelatedProductSyncMigration : Migration
{
    public override void Up()
    {
        if (!DataSettingsManager.IsDatabaseInstalled())
            return;

        var table = NameCompatibilityManager.GetTableName(typeof(AkeneoSyncProfile));

        if (!Schema.Table(table).Column(nameof(AkeneoSyncProfile.RelatedProductSyncModeId)).Exists())
        {
            Create.Column(nameof(AkeneoSyncProfile.RelatedProductSyncModeId))
                .OnTable(table)
                .AsInt32()
                .NotNullable()
                .WithDefaultValue((int)AkeneoCollectionSyncMode.Disabled);
        }

        if (!Schema.Table(table).Column(nameof(AkeneoSyncProfile.RelatedProductAssociationType)).Exists())
        {
            Create.Column(nameof(AkeneoSyncProfile.RelatedProductAssociationType))
                .OnTable(table)
                .AsString(100)
                .Nullable();
        }
    }

    public override void Down()
    {
        // Intentionally preserve sync profile configuration.
    }
}
