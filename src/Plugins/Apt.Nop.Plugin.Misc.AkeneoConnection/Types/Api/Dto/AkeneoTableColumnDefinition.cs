using System.Text.Json.Serialization;
namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
public sealed class AkeneoTableColumnDefinition
{
    [JsonPropertyName("code")]
    public string Code { get; set; }
    [JsonPropertyName("data_type")]
    public string DataType { get; set; }
}
