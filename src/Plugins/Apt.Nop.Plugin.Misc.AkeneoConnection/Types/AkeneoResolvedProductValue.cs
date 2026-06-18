using System.Text.Json;
namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
public class AkeneoResolvedProductValue
{
    public string AttributeCode { get; set; }

    public string Locale { get; set; }

    public string Channel { get; set; }

    public string Currency { get; set; }

    public JsonElement? RawData { get; set; }

    public string DisplayValue { get; set; }
    public IReadOnlyList<string> DisplayValues { get; set; }
}