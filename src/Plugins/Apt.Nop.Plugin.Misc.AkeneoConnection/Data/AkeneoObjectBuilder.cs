using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using FluentMigrator.Builders.Create.Table;
using Nop.Data.Mapping.Builders;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Data;
public class AkeneoFamilyVariantImportConfigurationBuilder : NopEntityBuilder<AkeneoFamilyMapping>
{
    public override void MapEntity(CreateTableExpressionBuilder table)
    {
        table.WithColumn(nameof(AkeneoFamilyMapping.AkeneoFamilyCode)).AsString(100);
    }
}
