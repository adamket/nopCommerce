using Nop.Core;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;

/// <summary>
/// Stores the durable binding between one source product and the nopCommerce
/// representation created for it by a specific sync profile.
/// </summary>
public class AkeneoProductSyncState : BaseEntity
{
    public int SyncProfileId { get; set; }

    public int AkeneoEntityTypeId { get; set; }

    public string AkeneoCode { get; set; }

    public string AkeneoUuid { get; set; }

    public string AkeneoParentCode { get; set; }

    public int DestinationKindId { get; set; }

    /// <summary>
    /// The destination product. For a combination this is the parent product.
    /// </summary>
    public int NopProductId { get; set; }

    public int? NopParentProductId { get; set; }

    public int? NopProductAttributeCombinationId { get; set; }

    public int? NopProductAttributeValueId { get; set; }

    public int LastSeenRunRecordId { get; set; }

    public DateTime LastSeenOnUtc { get; set; }

    public DateTime? LastSourceUpdatedOnUtc { get; set; }

    public string LastDesiredStateHash { get; set; }

    public int LifecycleStatusId { get; set; }

    public DateTime CreatedOnUtc { get; set; }

    public DateTime UpdatedOnUtc { get; set; }
}

public enum AkeneoProductDestinationKind
{
    NopProduct = 10,
    GroupedChildProduct = 20,
    AssociatedProduct = 30,
    ProductAttributeCombination = 40
}

public enum AkeneoProductLifecycleStatus
{
    Active = 10,
    MissingFromAuthoritativeScope = 20,
    Unpublished = 30,
    PurchasingDisabled = 40,
    Detached = 50,
    SoftDeleted = 60,
    Deleted = 70
}
