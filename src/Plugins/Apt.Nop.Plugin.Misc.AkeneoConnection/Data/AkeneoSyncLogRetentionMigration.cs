using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using FluentMigrator;
using Nop.Data;
using Nop.Data.Mapping;
using Nop.Data.Migrations;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Data;

/// <summary>
/// Adds per-profile sync log retention. New installs already get these
/// columns from the entity, so each one is only added when missing.
/// </summary>
[NopMigration(
    "2026-10-16 01:10:00",
    "AkeneoConnection: Sync profile log retention",
    MigrationProcessType.NoMatter)]
public sealed class AkeneoSyncLogRetentionMigration : Migration
{
    public override void Up()
    {
        if (!DataSettingsManager.IsDatabaseInstalled())
            return;

        var table = NameCompatibilityManager.GetTableName(typeof(AkeneoSyncProfile));

        if (!Schema.Table(table).Column(nameof(AkeneoSyncProfile.SyncLogRetentionEnabled)).Exists())
        {
            Create.Column(nameof(AkeneoSyncProfile.SyncLogRetentionEnabled))
                .OnTable(table)
                .AsBoolean()
                .NotNullable()
                .WithDefaultValue(false);
        }

        if (!Schema.Table(table).Column(nameof(AkeneoSyncProfile.SyncItemLogRetentionDays)).Exists())
        {
            Create.Column(nameof(AkeneoSyncProfile.SyncItemLogRetentionDays))
                .OnTable(table)
                .AsInt32()
                .NotNullable()
                .WithDefaultValue(30);
        }

        if (!Schema.Table(table).Column(nameof(AkeneoSyncProfile.SyncRunRecordRetentionDays)).Exists())
        {
            Create.Column(nameof(AkeneoSyncProfile.SyncRunRecordRetentionDays))
                .OnTable(table)
                .AsInt32()
                .NotNullable()
                .WithDefaultValue(90);
        }
    }

    public override void Down()
    {
        // Intentionally preserve sync profile configuration.
    }
}
