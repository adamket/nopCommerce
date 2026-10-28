using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Web.Framework.Models;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Models;

public record AkeneoCategoryMappingListModel : BaseNopModel
{
    public AkeneoCategoryMappingListModel()
    {
        Rows = new List<AkeneoCategoryMappingModel>();
        AvailableNopCategories = new List<SelectListItem>();
    }

    public IList<AkeneoCategoryMappingModel> Rows { get; set; }

    public IList<SelectListItem> AvailableNopCategories { get; set; }

    public IList<AkeneoCategoryContentMappingModel> ContentMappings { get; set; } = new List<AkeneoCategoryContentMappingModel>();

    /// <summary>Template attributes found on the Akeneo categories, for the content mapping editor.</summary>
    public IList<AkeneoCategoryTemplateAttributeModel> TemplateAttributes { get; set; } = new List<AkeneoCategoryTemplateAttributeModel>();

    /// <summary>Locales the Akeneo category labels use.</summary>
    public IList<string> LabelLocales { get; set; } = new List<string>();

    /// <summary>nopCommerce languages a mapping can write to, besides the standard property.</summary>
    public IList<SelectListItem> AvailableLanguages { get; set; } = new List<SelectListItem>();

    /// <summary>Each language's culture by ID, to show which language a translation locale writes.</summary>
    public IDictionary<int, string> LanguageCultures { get; set; } = new Dictionary<int, string>();

    /// <summary>Akeneo locales category content is translated into.</summary>
    public IList<string> TranslationLocales { get; set; } = new List<string>();

    /// <summary>Set when the category template values could not be read.</summary>
    public string ContentReadError { get; set; }
}