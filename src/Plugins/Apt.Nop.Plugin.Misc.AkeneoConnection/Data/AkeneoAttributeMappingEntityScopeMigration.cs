//using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
//using FluentMigrator;
//using Nop.Data.Mapping;
//using Nop.Data.Migrations;

//namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Data;

//[NopMigration(
//    "2026-07-20 02:00:00",
//    "AkeneoConnection: Add attribute mapping entity applicability",
//    MigrationProcessType.Update)]
//public class AkeneoAttributeMappingEntityScopeMigration : MigrationBase
//{
//    public override void Up()
//    {
//        var table = NameCompatibilityManager.GetTableName(
//            typeof(AkeneoAttributeMapping));

//        if (!Schema.Table(table).Exists() ||
//            Schema.Table(table)
//                .Column(nameof(AkeneoAttributeMapping.EntityScopeId))
//                .Exists())
//        {
//            return;
//        }

//        Alter.Table(table)
//            .AddColumn(nameof(AkeneoAttributeMapping.EntityScopeId))
//            .AsInt32()
//            .NotNullable()
//            .WithDefaultValue(
//                (int)AkeneoAttributeMappingEntityScope.All);
//    }

//    public override void Down()
//    {
//        // Preserve mapping applicability configuration on downgrade.
//    }
//}
