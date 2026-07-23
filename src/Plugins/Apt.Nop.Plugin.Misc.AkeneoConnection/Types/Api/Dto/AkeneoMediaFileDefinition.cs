using System.Text.Json.Serialization;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;

public class AkeneoMediaFileDefinition
{
    [JsonPropertyName("code")]
    public string Code { get; set; }

    [JsonPropertyName("original_filename")]
    public string OriginalFilename { get; set; }

    [JsonPropertyName("mime_type")]
    public string MimeType { get; set; }

    [JsonPropertyName("size")]
    public long? Size { get; set; }

    [JsonPropertyName("extension")]
    public string Extension { get; set; }
}
