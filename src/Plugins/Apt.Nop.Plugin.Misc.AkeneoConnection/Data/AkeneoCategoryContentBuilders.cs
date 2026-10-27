using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using FluentMigrator.Builders.Create.Table;
using Nop.Data.Mapping.Builders;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Data;

public class AkeneoCategoryContentMappingBuilder : NopEntityBuilder<AkeneoCategoryContentMapping>
{
    public override void MapEntity(CreateTableExpressionBuilder table)
    {
        table
            .WithColumn(nameof(AkeneoCategoryContentMapping.AkeneoAttributeCode)).AsString(255).Nullable()
            .WithColumn(nameof(AkeneoCategoryContentMapping.Locale)).AsString(20).Nullable()
            .WithColumn(nameof(AkeneoCategoryContentMapping.Channel)).AsString(100).Nullable();
    }
}

public class AkeneoCategoryContentStateBuilder : NopEntityBuilder<AkeneoCategoryContentState>
{
    public override void MapEntity(CreateTableExpressionBuilder table)
    {
        table
            .WithColumn(nameof(AkeneoCategoryContentState.AkeneoCategoryCode)).AsString(255).NotNullable()
            .WithColumn(nameof(AkeneoCategoryContentState.PictureFilePath)).AsString(1000).Nullable()
            .WithColumn(nameof(AkeneoCategoryContentState.LastMessage)).AsString(4000).Nullable();
    }
}
