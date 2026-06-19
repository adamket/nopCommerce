using System.Text.Json;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api;

public class AkeneoProductPageResult
{
    public IList<JsonElement> Items { get; set; } = new List<JsonElement>();

    public string SearchAfter { get; set; }

    public bool HasNextPage => !string.IsNullOrWhiteSpace(SearchAfter);
}