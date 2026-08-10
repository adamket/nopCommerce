using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using FluentMigrator;
using Nop.Data;
using Nop.Data.Mapping;
using Nop.Data.Migrations;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Data;

[NopMigration(
    "2026-08-03 11:35:00",
    "AkeneoConnection: Add mapping-level specification missing-value handling",
    MigrationProcessType.NoMatter)]
public sealed class AkeneoSpecificationMissingValueHandlingMigration
    : MigrationBase
{
    public override void Up()
    {
        if (!DataSettingsManager.IsDatabaseInstalled())
            return;

        var table = NameCompatibilityManager.GetTableName(
            typeof(AkeneoAttributeMapping));
        var column = nameof(
            AkeneoAttributeMapping.SpecificationMissingValueHandlingId);

        if (!Schema.Table(table).Exists() ||
            Schema.Table(table).Column(column).Exists())
        {
            return;
        }

        Alter.Table(table)
            .AddColumn(column)
            .AsInt32()
            .NotNullable()
            .WithDefaultValue(
                (int)AkeneoSpecificationMissingValueHandling
                    .CreateSpecificationAttributeOption);
    }

    public override void Down()
    {
        // Preserve mapping behavior during plugin downgrade/uninstall.
    }
}
