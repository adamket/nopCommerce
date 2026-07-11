using System.Text.Json.Serialization;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api;

public class PagedCollection<T>
{
    [JsonPropertyName("_links")]
    public PagedCollectionLinks Links { get; set; }

    [JsonPropertyName("_embedded")]
    public PagedCollectionEmbedded<T> Embedded { get; set; }
}

public class PagedCollectionEmbedded<T>
{
    [JsonPropertyName("items")]
    public List<T> Items { get; set; } = new();
}

public class PagedCollectionLinks
{
    [JsonPropertyName("next")]
    public PagedCollectionLink Next { get; set; }
}

public class PagedCollectionLink
{
    [JsonPropertyName("href")]
    public string Href { get; set; }
}