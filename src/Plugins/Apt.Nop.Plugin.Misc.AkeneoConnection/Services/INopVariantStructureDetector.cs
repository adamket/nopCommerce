using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Nop.Core.Domain.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
public interface INopVariantStructureDetector
{
    Task<NopVariantStructureDetectionResult> DetectAsync(Product parentProduct);
}
