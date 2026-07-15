using Nop.Core.Domain.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
public class ProductAttributeValueChangeResult
{
    public ProductAttributeValue Value { get; set; }
    public bool Changed { get; set; }
}
