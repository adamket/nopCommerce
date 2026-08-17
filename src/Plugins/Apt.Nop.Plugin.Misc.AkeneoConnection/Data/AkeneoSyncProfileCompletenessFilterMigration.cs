using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using FluentMigrator;
using Nop.Data;
using Nop.Data.Mapping;
using Nop.Data.Migrations;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Data;

[NopMigration(
    "2026-08-17 02:30:00",
    "AkeneoConnection: Add sync profile completeness filter",
    MigrationProcessType.NoMatter)]
public sealed class AkeneoSyncProfileCompletenessFilterMigration
    : MigrationBase
{
    public override void Up()
    {
        if (!DataSettingsManager.IsDatabaseInstalled())
            return;

        var table = NameCompatibilityManager.GetTableName(
            typeof(AkeneoSyncProfile));
        var column = nameof(AkeneoSyncProfile.CompletenessFilterId);

        if (!Schema.Table(table).Exists() ||
            Schema.Table(table).Column(column).Exists())
        {
            return;
        }

        Alter.Table(table)
            .AddColumn(column)
            .AsInt32()
            .NotNullable()
            .WithDefaultValue((int)AkeneoCompletenessFilter.None);
    }

    public override void Down()
    {
        // Preserve sync profile configuration during plugin downgrade/uninstall.
    }
}
