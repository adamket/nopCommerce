using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api;

public class AkeneoProductPageResult
{
    public IList<AkeneoProductDefinition> Items { get; set; } = new List<AkeneoProductDefinition>();

    public string SearchAfter { get; set; }

    public bool HasNextPage => !string.IsNullOrWhiteSpace(SearchAfter);
}