//using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
//using FluentMigrator;
//using Nop.Data.Mapping;
//using Nop.Data.Migrations;

//namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Data;

//[NopMigration(
//    "2026-07-20 01:00:00",
//    "AkeneoConnection: Add reference entity field mappings",
//    MigrationProcessType.Update)]
//public class AkeneoReferenceEntityFieldMappingMigration : MigrationBase
//{
//    public override void Up()
//    {
//        var table = NameCompatibilityManager.GetTableName(
//            typeof(AkeneoAttributeMapping));

//        if (!Schema.Table(table).Exists())
//            return;

//        if (!Schema.Table(table)
//                .Column(nameof(AkeneoAttributeMapping.AkeneoReferenceEntityCode))
//                .Exists())
//        {
//            Alter.Table(table)
//                .AddColumn(nameof(AkeneoAttributeMapping.AkeneoReferenceEntityCode))
//                .AsString(255)
//                .Nullable();
//        }

//        if (!Schema.Table(table)
//                .Column(nameof(AkeneoAttributeMapping.AkeneoReferenceEntityAttributeCode))
//                .Exists())
//        {
//            Alter.Table(table)
//                .AddColumn(nameof(AkeneoAttributeMapping.AkeneoReferenceEntityAttributeCode))
//                .AsString(255)
//                .Nullable();
//        }
//    }

//    public override void Down()
//    {
//        // Keep mapping configuration on downgrade.
//    }
//}
