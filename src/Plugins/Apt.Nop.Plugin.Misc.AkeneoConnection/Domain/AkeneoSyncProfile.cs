using Nop.Core;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;

public class AkeneoSyncProfile : BaseEntity
{
    // Identity
    public string Name { get; set; }

    public bool Enabled { get; set; }

    // Akeneo value context
    public string AkeneoChannel { get; set; }

    public string AkeneoLocales { get; set; }

    public string CurrencyCode { get; set; }

    // Product write policy
    public int ProductWriteModeId { get; set; }

    public int UnmappedAttributeBehaviorId { get; set; }

    // Missing scalar value policy
    public int ProductFieldMissingValueBehaviorId { get; set; }

    public int SeoFieldMissingValueBehaviorId { get; set; }

    public int CustomPropertyMissingValueBehaviorId { get; set; }

    // Collection synchronization policy
    public int CategorySyncModeId { get; set; }

    public int SpecificationAttributeSyncModeId { get; set; }

    public int ProductAttributeSyncModeId { get; set; }

    public int AssetSyncModeId { get; set; }

    // Authoritative full-run lifecycle policy
    public int MissingProductBehaviorId { get; set; }

    // Destination metadata creation
    public bool CreateMissingSpecificationAttributeOptions { get; set; }

    public bool CreateMissingProductAttributeValues { get; set; }

    // Batch execution
    public int PageSize { get; set; }

    public int? MaxProducts { get; set; }

    public bool ContinueOnError { get; set; }

    public bool SaveRawPayloadSnapshot { get; set; }

    // Akeneo filters
    public string AkeneoFamilyCodes { get; set; }

    public string AkeneoCategoryCodes { get; set; }

    public string AkeneoProductGroupCodes { get; set; }

    public int CategoryFilterModeId { get; set; }

    public int ProductEnabledFilterId { get; set; }

    public int ProductParentFilterModeId { get; set; }

    public int UpdatedFilterModeId { get; set; }

    /// <summary>
    /// Uses Akeneo SaaS Enterprise linked-entity date filtering so asset-only
    /// changes can revisit their linked products during a delta run.
    /// </summary>
    public bool IncludeLinkedAssetUpdates { get; set; }

    public DateTime? UpdatedAfterUtc { get; set; }

    public int? UpdatedSinceLastNDays { get; set; }

    public string AdditionalSearchJson { get; set; }

    // Audit
    public DateTime CreatedOnUtc { get; set; }

    public DateTime UpdatedOnUtc { get; set; }
}

public enum AkeneoProductWriteMode
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


public enum AkeneoUpdatedFilterMode
{
    None = 0,
    FixedDate = 10,
    RollingDays = 20,
    SinceLastSuccessfulRun = 30
}

public enum AkeneoCollectionSyncMode
{
    Disabled = 0,

    /// <summary>
    /// Add missing Akeneo values but preserve additional nopCommerce values.
    /// </summary>
    Merge = 10,

    /// <summary>
    /// Remove only stale relationships previously created and owned by this plugin.
    /// </summary>
    ReplaceManaged = 20,

    /// <summary>
    /// Make the whole nopCommerce collection match Akeneo, including deleting
    /// manually maintained relationships. This is intentionally explicit.
    /// </summary>
    ReplaceAll = 30
}

public enum AkeneoMissingValueBehavior
{
    PreserveExisting = 0,
    ClearExisting = 10
}

public enum AkeneoMissingProductBehavior
{
    Ignore = 0,
    Unpublish = 10,
    DisablePurchasing = 20,
    DetachFromParent = 30,
    SoftDelete = 40,
    Delete = 50
}