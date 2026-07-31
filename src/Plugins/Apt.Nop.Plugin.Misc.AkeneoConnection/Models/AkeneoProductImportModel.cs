using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
public class AkeneoProductImportModel
{
    public string Uuid { get; set; }

    public string ProductModelCode { get; set; }

    public int AkeneoEntityTypeId { get; set; } = (int)AkeneoEntityType.Product;

    public string Locale { get; set; }

    public string Channel { get; set; }

    public string Currency { get; set; }
}
