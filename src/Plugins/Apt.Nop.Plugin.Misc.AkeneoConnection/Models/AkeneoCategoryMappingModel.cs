using Nop.Web.Framework.Models;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Models;

public record AkeneoCategoryMappingModel : BaseNopModel
{
    public string AkeneoCode { get; set; }

    public string AkeneoLabel { get; set; }

    public string AkeneoParentCode { get; set; }

    public int Level { get; set; }

    public int NopCategoryId { get; set; }

    public string NopCategoryName { get; set; }

    public bool IsMapped => NopCategoryId > 0;
}