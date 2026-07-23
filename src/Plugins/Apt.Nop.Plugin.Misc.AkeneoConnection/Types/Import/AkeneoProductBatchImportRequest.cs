using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;

public class AkeneoProductBatchImportRequest : AkeneoProductImportRequest
{
    public int PageSize { get; set; } = 100;

    public int? MaxProducts { get; set; }

    public bool ContinueOnError { get; set; } = true;

    public IList<string> AkeneoCategoryCodes { get; set; } = new List<string>();

    public AkeneoCategoryFilterMode CategoryFilterMode { get; set; }

    public IList<string> AkeneoFamilyCodes { get; set; } = new List<string>();

    public IList<string> AkeneoProductGroupCodes { get; set; } = new List<string>();

    public AkeneoProductEnabledFilter ProductEnabledFilter { get; set; }

    public DateTime? UpdatedAfterUtc { get; set; }

    public bool IncludeLinkedAssetUpdates { get; set; }

    public int? UpdatedSinceLastNDays { get; set; }

    public AkeneoProductParentFilterMode ProductParentFilterMode { get; set; }

    public string AdditionalSearchJson { get; set; }

    public string SearchJson { get; set; }

    public string SearchAfter { get; set; }

    public UnmappedAkeneoAttributeBehavior UnmappedAttributeBehavior { get; set; }

    // defaultwarehouseid
    // defaulttaxcategoryid
}