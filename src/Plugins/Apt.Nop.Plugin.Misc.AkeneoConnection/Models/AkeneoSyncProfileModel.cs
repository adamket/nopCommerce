using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Web.Framework.Models;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Models;

public record AkeneoSyncProfileModel : BaseNopEntityModel
{
    public string Name { get; set; }

    public bool Enabled { get; set; }

    public string AkeneoChannel { get; set; }

    /// <summary>The primary Akeneo locale: its values go into the base product fields.</summary>
    public string AkeneoLocale { get; set; }

    /// <summary>
    /// Further locales, saved after the primary one in AkeneoLocales. Each writes
    /// nopCommerce localized values for the language with the matching culture.
    /// </summary>
    public IList<string> AdditionalAkeneoLocales { get; set; } = new List<string>();

    /// <summary>nopCommerce languages (value = culture such as es-US), to show which one each additional locale writes.</summary>
    public IList<SelectListItem> AvailableNopLanguages { get; set; } = new List<SelectListItem>();

    public IList<string> SelectedAkeneoFamilyCodes { get; set; } = new List<string>();
    public IList<string> SelectedAkeneoCategoryCodes { get; set; } = new List<string>();
    public IList<string> SelectedAkeneoProductGroupCodes { get; set; } = new List<string>();

    public IList<SelectListItem> AvailableAkeneoChannels { get; set; } = new List<SelectListItem>();

    public IList<SelectListItem> AvailableAkeneoLocales { get; set; } = new List<SelectListItem>();

    public int ProductWriteModeId { get; set; }

    public string ProductWriteModeName { get; set; }

    public int UnmappedAttributeBehaviorId { get; set; }

    public string UnmappedAttributeBehaviorName { get; set; }

    public int PageSize { get; set; }

    [UIHint("Int32Nullable")]
    public int? MaxProducts { get; set; }

    public bool ContinueOnError { get; set; }

    public bool SyncLogRetentionEnabled { get; set; }

    public int? SyncItemLogRetentionDays { get; set; }

    public int? SyncRunRecordRetentionDays { get; set; }


   // public bool AddMappedManufacturers { get; set; }

    public bool CreateMissingProductAttributeValues { get; set; }

    public string AkeneoFamilyCodes { get; set; }

    public string AkeneoCategoryCodes { get; set; }

    public int CategoryFilterModeId { get; set; }

    public string CategoryFilterModeName { get; set; }

    public int ProductEnabledFilterId { get; set; }

    public string ProductEnabledFilterName { get; set; }

    public int CompletenessFilterId { get; set; }

    public string CompletenessFilterName { get; set; }

    [UIHint("DateTimeNullable")]
    public DateTime? UpdatedAfter { get; set; }

    public string UpdatedAfterTimeZoneDisplayName { get; set; }

    [UIHint("Int32Nullable")]

    public int? UpdatedSinceLastNDays { get; set; }

    public int ProductParentFilterModeId { get; set; }

    public string ProductParentFilterModeName { get; set; }

    public string AdditionalSearchJson { get; set; }
    public string AkeneoProductGroupCodes { get; set; }

    public IList<SelectListItem> AvailableProductWriteModes { get; set; } = new List<SelectListItem>();

    public IList<SelectListItem> AvailableUnmappedAttributeBehaviors { get; set; } = new List<SelectListItem>();

    public IList<SelectListItem> AvailableCategoryFilterModes { get; set; } = new List<SelectListItem>();

    public IList<SelectListItem> AvailableProductEnabledFilters { get; set; } = new List<SelectListItem>();

    public IList<SelectListItem> AvailableCompletenessFilters { get; set; } = new List<SelectListItem>();

    public IList<SelectListItem> AvailableProductParentFilterModes { get; set; } = new List<SelectListItem>();
    public IList<SelectListItem> AvailableCategorySyncModes { get; set; } = new List<SelectListItem>();
    public IList<SelectListItem> AvailableTagSyncModes { get; set; } = new List<SelectListItem>();
    public IList<SelectListItem> AvailableRelatedProductSyncModes { get; set; } = new List<SelectListItem>();
    public IList<SelectListItem> AvailableCrossSellSyncModes { get; set; } = new List<SelectListItem>();
    public IList<SelectListItem> AvailableSpecificationAttributeSyncModes { get; set; } = new List<SelectListItem>();
    public IList<SelectListItem> AvailableProductAttributeSyncModes { get; set; } = new List<SelectListItem>();
    public IList<SelectListItem> AvailableAssetSyncModes { get; set; } = new List<SelectListItem>();
    public IList<SelectListItem> AvailableMissingProductBehaviors { get; set; } = new List<SelectListItem>();

 

    public IList<SelectListItem> AvailableAkeneoProductGroups { get; set; } = new List<SelectListItem>();
    public IList<SelectListItem> AvailableAkeneoFamilies { get; set; } = new List<SelectListItem>();
    public IList<SelectListItem> AvailableAkeneoCategories { get; set; } = new List<SelectListItem>();

    public string CurrencyCode { get; set; }

    public string SkipRulesJson { get; set; }


    public int ProductFieldMissingValueBehaviorId { get; set; }

    public int SeoFieldMissingValueBehaviorId { get; set; }

    public int CustomPropertyMissingValueBehaviorId { get; set; }

    public int CategorySyncModeId { get; set; }

    public int TagSyncModeId { get; set; }

    public int RelatedProductSyncModeId { get; set; }

    public string RelatedProductAssociationType { get; set; }

    public int CrossSellSyncModeId { get; set; }

    public string CrossSellAssociationType { get; set; }

    public int SpecificationAttributeSyncModeId { get; set; }

    public int ProductAttributeSyncModeId { get; set; }

    public int AssetSyncModeId { get; set; }

    public int MissingProductBehaviorId { get; set; }

    public int UpdatedFilterModeId { get; set; }

    public bool IncludeLinkedAssetUpdates { get; set; }

    /// <summary>Set on edit when mappings or this profile changed after its last whole-profile resync.</summary>
    public bool ResyncRecommended { get; set; }

    /// <summary>When the mappings or profile last changed, in store time.</summary>
    public string ConfigurationChangedOn { get; set; }

    /// <summary>When the last whole-profile resync started, in store time; null if none has run.</summary>
    public string LastResyncOn { get; set; }

    public IList<SelectListItem>
        AvailableProductFieldMissingValueBehaviors
    { get; set; }
        = new List<SelectListItem>();

    public IList<SelectListItem>
        AvailableSeoFieldMissingValueBehaviors
    { get; set; }
        = new List<SelectListItem>();

    public IList<SelectListItem>
        AvailableCustomPropertyMissingValueBehaviors
    { get; set; }
        = new List<SelectListItem>();

   
   

    public IList<SelectListItem>
        AvailableUpdatedFilterModes
    { get; set; }
        = new List<SelectListItem>();


}