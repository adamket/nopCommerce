

using System.Data;
using Apt.Nop.Plugin.Misc.NopCms.Domain;
using FluentMigrator.Builders.Create.Table;
using Nop.Core.Domain.Topics;
using Nop.Data.Extensions;
using Nop.Data.Mapping.Builders;

namespace Apt.Nop.Plugin.Misc.NopCms.Data;
public class TopicEntryRecordBuilder : NopEntityBuilder<TopicEntry>
{
    /// <summary>
    /// Apply entity configuration
    /// </summary>
    /// <param name="table">Create table expression builder</param>
    public override void MapEntity(CreateTableExpressionBuilder table)
    {
        table.WithColumn(nameof(TopicEntry.Id)).AsInt32().Identity().PrimaryKey()
            .WithColumn(nameof(TopicEntry.TopicId)).AsInt32().ForeignKey<Topic>(onDelete: Rule.Cascade)
            .WithColumn(nameof(TopicEntry.Body)).AsString(int.MaxValue)
            .WithColumn(nameof(TopicEntry.Title)).AsString(int.MaxValue)
            .WithColumn(nameof(TopicEntry.Version)).AsInt32()
            .WithColumn(nameof(TopicEntry.TopicEntryStatusId)).AsInt32();

    }
}