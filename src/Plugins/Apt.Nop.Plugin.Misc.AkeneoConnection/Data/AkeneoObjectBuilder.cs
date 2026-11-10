using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using FluentMigrator.Builders.Create.Table;
using Nop.Data.Mapping.Builders;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Data;
public class AkeneoFamilyVariantImportConfigurationBuilder : NopEntityBuilder<AkeneoFamilyMapping>
{
    public override void MapEntity(CreateTableExpressionBuilder table)
    {
        table.WithColumn(nameof(AkeneoFamilyMapping.AkeneoFamilyCode)).AsString(100);
        table.WithColumn(nameof(AkeneoFamilyMapping.AkeneoFamilyVariantCode))
            .AsString(100)
            .NotNullable()
            .WithDefaultValue(string.Empty);
    }
}

public class AkeneoFamilyVariantAxisMappingBuilder : NopEntityBuilder<AkeneoFamilyVariantAxisMapping>
{
    public override void MapEntity(CreateTableExpressionBuilder table) =>
        table.WithColumn(nameof(AkeneoFamilyVariantAxisMapping.OptionNameTemplate)).AsString(int.MaxValue).Nullable();
}

public class AkeneoSyncProfileBuilder : NopEntityBuilder<AkeneoSyncProfile>
{
    public override void MapEntity(CreateTableExpressionBuilder table) =>
        table.WithColumn(nameof(AkeneoSyncProfile.LogSkips)).AsBoolean().NotNullable().WithDefaultValue(false);
}
