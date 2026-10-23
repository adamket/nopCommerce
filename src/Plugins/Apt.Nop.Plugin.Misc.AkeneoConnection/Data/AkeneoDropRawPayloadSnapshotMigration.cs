using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using FluentMigrator;
using Nop.Data;
using Nop.Data.Mapping;
using Nop.Data.Migrations;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Data;

/// <summary>
/// Drops the raw Akeneo payload snapshot: item logs stored it but nothing ever
/// displayed it, and the profile toggle that enabled it is gone.
/// </summary>
[NopMigration(
    "2026-10-24 00:00:00",
    "AkeneoConnection: Drop raw payload snapshot columns",
    MigrationProcessType.NoMatter)]
public sealed class AkeneoDropRawPayloadSnapshotMigration : Migration
{
    public override void Up()
    {
        if (!DataSettingsManager.IsDatabaseInstalled())
            return;

        DropColumnIfExists(
            NameCompatibilityManager.GetTableName(typeof(AkeneoSyncItemLog)),
            "RawPayloadSnapshot");
        DropColumnIfExists(
            NameCompatibilityManager.GetTableName(typeof(AkeneoSyncProfile)),
            "SaveRawPayloadSnapshot");
    }

    public override void Down()
    {
        // The snapshot was never read, so there is nothing to restore.
    }

    private void DropColumnIfExists(string table, string column)
    {
        if (Schema.Table(table).Exists() && Schema.Table(table).Column(column).Exists())
            Delete.Column(column).FromTable(table);
    }
}
