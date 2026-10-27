using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using FluentMigrator;
using Nop.Data;
using Nop.Data.Mapping;
using Nop.Data.Migrations;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Data;

/// <summary>
/// Adds the sub-model rule settings that make a matching sub-model the
/// nopCommerce parent. Existing rules keep the family's parent level. A fresh
/// install creates them with the table, so each column is added only when missing.
/// </summary>
[NopMigration(
    "2026-10-27 00:00:00",
    "AkeneoConnection: Add sub-model parent rule columns",
    MigrationProcessType.NoMatter)]
public sealed class AkeneoSubModelParentMigration : Migration
{
    public override void Up()
    {
        if (!DataSettingsManager.IsDatabaseInstalled())
            return;

        var table = NameCompatibilityManager.GetTableName(typeof(AkeneoFamilySubModelRule));
        if (!Schema.Table(table).Exists())
            return;

        if (!Schema.Table(table).Column(nameof(AkeneoFamilySubModelRule.ParentLevelId)).Exists())
        {
            Create.Column(nameof(AkeneoFamilySubModelRule.ParentLevelId))
                .OnTable(table)
                .AsInt32()
                .NotNullable()
                .WithDefaultValue((int)AkeneoSubModelParentLevel.FamilySetting);
        }

        if (!Schema.Table(table).Column(nameof(AkeneoFamilySubModelRule.AssociatedProductAttributeId)).Exists())
        {
            Create.Column(nameof(AkeneoFamilySubModelRule.AssociatedProductAttributeId))
                .OnTable(table)
                .AsInt32()
                .Nullable();
        }

        if (!Schema.Table(table).Column(nameof(AkeneoFamilySubModelRule.AssociatedValueNameTemplate)).Exists())
        {
            Create.Column(nameof(AkeneoFamilySubModelRule.AssociatedValueNameTemplate))
                .OnTable(table)
                .AsString(int.MaxValue)
                .Nullable();
        }

        if (!Schema.Table(table).Column(nameof(AkeneoFamilySubModelRule.HideChildProducts)).Exists())
        {
            Create.Column(nameof(AkeneoFamilySubModelRule.HideChildProducts))
                .OnTable(table)
                .AsBoolean()
                .NotNullable()
                .WithDefaultValue(true);
        }
    }

    public override void Down()
    {
        // Leaving the columns is harmless; nothing reads them once the plugin is removed.
    }
}
