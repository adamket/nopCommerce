using Nop.Core;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;

public class AkeneoSyncProfile : BaseEntity
{
    public string Name { get; set; }

    public bool Enabled { get; set; }

    public string AkeneoChannel { get; set; }

    // CSV: en_US,fr_FR
    public string AkeneoLocales { get; set; }

   // public string RootCategoryCode { get; set; }

    public int ImportModeId { get; set; }

    public int UnmappedAttributeBehaviorId { get; set; } // currently unused (always ignore unmapped attributes)

    // Batch behavior
    public int PageSize { get; set; }

    public int? MaxProducts { get; set; }

    public bool ContinueOnError { get; set; }

    public bool SaveRawPayloadSnapshot { get; set; }

    // Import behavior
    public bool AddMappedCategories { get; set; }

    public bool AddMappedManufacturers { get; set; }

    public bool CreateMissingSpecificationAttributeOptions { get; set; }

    public bool CreateMissingProductAttributeValues { get; set; }

    // Akeneo product filters
    public string AkeneoFamilyCodes { get; set; }

    public string AkeneoCategoryCodes { get; set; }

    public string AkeneoProductGroupCodes { get; set; }

    public int CategoryFilterModeId { get; set; }

    public int ProductEnabledFilterId { get; set; }

    public DateTime? UpdatedAfterUtc { get; set; }

    public int? UpdatedSinceLastNDays { get; set; }

    public int ProductParentFilterModeId { get; set; }

    // Advanced override for users who know Akeneo search JSON.
    public string AdditionalSearchJson { get; set; }

    public DateTime CreatedOnUtc { get; set; }

    public DateTime UpdatedOnUtc { get; set; }
}


public enum AkeneoImportMode
{
    CreateAndUpdate = 10,
    CreateOnly = 20,
    UpdateOnly = 30
}

public enum UnmappedAkeneoAttributeBehavior
{
    Ignore = 0,
    Log = 10,
    ImportAsCustomProperty = 20,
    ImportAsSpecificationAttribute = 30
}

public enum AkeneoCategoryFilterMode
{
    None = 0,

    // Products directly in the selected categories
    In = 10,

    // Products in the selected categories or their children
    InChildren = 20,

    // Products not in the selected categories
    NotIn = 30,

    // Products not in the selected categories or their children
    NotInChildren = 40,

    // Akeneo unclassified products
    Unclassified = 50,

    // Products in category scope or unclassified
    InOrUnclassified = 60
}

public enum AkeneoProductEnabledFilter
{
    Any = 0,
    EnabledOnly = 10,
    DisabledOnly = 20
}

public enum AkeneoProductParentFilterMode
{
    Any = 0,
    SimpleProductsOnly = 10,
    VariantProductsOnly = 20
}