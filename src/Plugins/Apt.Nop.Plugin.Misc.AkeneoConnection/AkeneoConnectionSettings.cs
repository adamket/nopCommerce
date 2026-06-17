using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Core.Configuration;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection;

/// <summary>
/// Represents settings of "Check money order" payment plugin
/// </summary>
public class AkeneoConnectionSettings : ISettings
{

    public string AkeneoConnectionClientId { get; set; }
    public string AkeneoConnectionClientSecret { get; set; }
    public string AkeneoConnectionBaseUrl { get; set; }
    public string AkeneoConnectionUsername { get; set; }
    public string AkeneoConnectionPassword { get; set; }

    public string DefaultChannelCode { get; set; } = string.Empty;
    public string DefaultLocaleCode { get; set; } = string.Empty;
    public string DefaultCurrencyCode { get; set; } = string.Empty;



}