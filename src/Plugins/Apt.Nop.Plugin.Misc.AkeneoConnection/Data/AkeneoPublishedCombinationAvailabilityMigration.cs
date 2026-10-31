using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using FluentMigrator;
using Nop.Data;
using Nop.Data.Mapping;
using Nop.Data.Migrations;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Data;

// Separate from the repeatable consolidated schema: switch existing managed
// combinations once, without overriding a later administrator selection.
[NopMigration("2026-10-30 13:00:00",
    "AkeneoConnection: Use Published rules for managed combination availability",
    MigrationProcessType.NoMatter)]
public sealed class AkeneoPublishedCombinationAvailabilityMigration : Migration
{
    public override void Up()
    {
        if (!DataSettingsManager.IsDatabaseInstalled())
            return;

        Update.Table(NameCompatibilityManager.GetTableName(typeof(AkeneoFamilyMapping)))
            .Set(new { CombinationAvailabilitySourceId = (int)AkeneoCombinationAvailabilitySource.PublishedRule })
            .Where(new
            {
                VariantRelationshipModeId = (int)AkeneoVariantRelationshipMode.ProductAttributeCombinations,
                ManageVariantsByEnabledStatus = true,
                CombinationAvailabilitySourceId = (int)AkeneoCombinationAvailabilitySource.EnabledStatus
            });
    }

    public override void Down()
    {
        // Preserve the administrator's saved availability choices.
    }
}
