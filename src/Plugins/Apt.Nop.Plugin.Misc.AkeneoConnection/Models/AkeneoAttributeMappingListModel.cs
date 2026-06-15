using Microsoft.AspNetCore.Mvc.Rendering;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Models;

public class AkeneoAttributeMappingListModel
{
    public int SelectedChannelId { get; set; }
    public int SelectedLocaleId { get; set; }
    public int SelectedCurrencyId { get; set; }

    public IList<SelectListItem> AvailableChannels { get; set; } = new List<SelectListItem>();
    public IList<SelectListItem> AvailableLocales { get; set; } = new List<SelectListItem>();
    public IList<SelectListItem> AvailableCurrencies { get; set; } = new List<SelectListItem>();
    public IList<SelectListItem> AvailableTargetTypes { get; set; } = new List<SelectListItem>();

    public IList<SelectListItem> AvailableSpecificationAttributes { get; set; } = new List<SelectListItem>();

    public IList<SelectListItem> AvailableProductAttributes { get; set; } = new List<SelectListItem>();

    public IList<AkeneoAttributeMappingModel> Mappings { get; set; } = new List<AkeneoAttributeMappingModel>();

    public IList<string> Warnings { get; set; } = new List<string>();
}