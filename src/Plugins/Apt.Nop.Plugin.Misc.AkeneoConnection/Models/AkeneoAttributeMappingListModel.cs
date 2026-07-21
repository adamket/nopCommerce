using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Models;

public class AkeneoAttributeMappingListModel
{
    public string AkeneoFamilyCode { get; set; }

    public IList<SelectListItem> AvailableAkeneoFamilies { get; set; }
        = new List<SelectListItem>();

    public string SelectedChannelCode { get; set; } = string.Empty;
    public string SelectedLocaleCode { get; set; } = string.Empty;
    public string SelectedCurrencyCode { get; set; } = string.Empty;
    public IList<SelectListItem> AvailableChannels { get; set; } = new List<SelectListItem>();
    public IList<SelectListItem> AvailableLocales { get; set; } = new List<SelectListItem>();
    public IList<SelectListItem> AvailableCurrencies { get; set; } = new List<SelectListItem>();
    public IList<SelectListItem> AvailableTargetTypes { get; set; } = new List<SelectListItem>();
    public IList<SelectListItem> AvailableSpecificationAttributes { get; set; } = new List<SelectListItem>();
    public IList<SelectListItem> AvailableProductAttributes { get; set; } = new List<SelectListItem>();
    /// <summary>
    /// Standard, computed, and saved reference-entity field mappings.
    /// Unlike a standard attribute row, an unmapped reference-entity attribute
    /// does not create a placeholder mapping here.
    /// </summary>
    public IList<AkeneoAttributeMappingModel> Mappings { get; set; }
        = new List<AkeneoAttributeMappingModel>();

    /// <summary>
    /// Reference-entity attribute metadata used to render containers independently
    /// of the zero-or-more saved field mappings in <see cref="Mappings"/>.
    /// </summary>
    public IList<AkeneoAttributeDefinitionModel> ReferenceEntityAttributes { get; set; }
        = new List<AkeneoAttributeDefinitionModel>();

    public IList<AkeneoAttributeMappingSourceOptionModel> AvailableFallbackSources { get; set; } = new List<AkeneoAttributeMappingSourceOptionModel>();
    public IList<string> Warnings { get; set; } = new List<string>();

    // Target Key options per NopTargetType (keyed by the int target-type as a string),
    // emitted to the view so Vue can rebuild the Target Key select client-side.
    public IDictionary<string, IList<NopTargetKeyOptionModel>> NopTargetKeyMap { get; set; }
        = new Dictionary<string, IList<NopTargetKeyOptionModel>>();
}

public class NopTargetKeyOptionModel
{
    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}