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

    /// <summary>The last category content sync's outcome for this category, or null if it never ran.</summary>
    public int? ContentStatusId { get; set; }

    public string ContentMessage { get; set; }

    public DateTime? ContentSyncedOnUtc { get; set; }
}