using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Web.Framework.Models;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Models;

public record AkeneoSyncProfileModel : BaseNopEntityModel
{
    public string Name { get; set; }

    public bool Enabled { get; set; }

    public string AkeneoChannel { get; set; }

    public string AkeneoLocales { get; set; }

    public IList<string> SelectedAkeneoLocaleCodes { get; set; } = new List<string>();
    public IList<string> SelectedAkeneoFamilyCodes { get; set; } = new List<string>();
    public IList<string> SelectedAkeneoCategoryCodes { get; set; } = new List<string>();
    public IList<string> SelectedAkeneoProductGroupCodes { get; set; } = new List<string>();

    public IList<SelectListItem> AvailableAkeneoChannels { get; set; } = new List<SelectListItem>();

    public IList<SelectListItem> AvailableAkeneoLocales { get; set; } = new List<SelectListItem>();

    //public string RootCategoryCode { get; set; }

    public int ProductWriteModeId { get; set; }

    public string ProductWriteModeName { get; set; }

    public int UnmappedAttributeBehaviorId { get; set; }

    public string UnmappedAttributeBehaviorName { get; set; }

    public int PageSize { get; set; }

    [UIHint("Int32Nullable")]
    public int? MaxProducts { get; set; }

    public bool ContinueOnError { get; set; }

    public bool SaveRawPayloadSnapshot { get; set; }


   // public bool AddMappedManufacturers { get; set; }

    public bool CreateMissingSpecificationAttributeOptions { get; set; }

    public bool CreateMissingProductAttributeValues { get; set; }

    public string AkeneoFamilyCodes { get; set; }

    public string AkeneoCategoryCodes { get; set; }

    public int CategoryFilterModeId { get; set; }

    public string CategoryFilterModeName { get; set; }

    public int ProductEnabledFilterId { get; set; }

    public string ProductEnabledFilterName { get; set; }

    [UIHint("DateTimeNullable")]
    public DateTime? UpdatedAfterUtc { get; set; }

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

    public IList<SelectListItem> AvailableProductParentFilterModes { get; set; } = new List<SelectListItem>();
    public IList<SelectListItem> AvailableSyncModes { get; set; } = new List<SelectListItem>();

 

    public IList<SelectListItem> AvailableAkeneoProductGroups { get; set; } = new List<SelectListItem>();
    public IList<SelectListItem> AvailableAkeneoFamilies { get; set; } = new List<SelectListItem>();
    public IList<SelectListItem> AvailableAkeneoCategories { get; set; } = new List<SelectListItem>();

    public string CurrencyCode { get; set; }

    public int ProductFieldMissingValueBehaviorId { get; set; }

    public int SeoFieldMissingValueBehaviorId { get; set; }

    public int CustomPropertyMissingValueBehaviorId { get; set; }

    public int CategorySyncModeId { get; set; }

    public int SpecificationAttributeSyncModeId { get; set; }

    public int ProductAttributeSyncModeId { get; set; }

    public int UpdatedFilterModeId { get; set; }

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