using Nop.Core.Configuration;

namespace Apt.Nop.Plugin.AkeneoConnection;

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

    public string AkeneoConnectionChannel { get; set; }
    public string AkeneoConnectionLocale { get; set; }
   // public int AkeneoConnectionDefaultSto

    //Channel
    // Locale(s)
    // Default store
    // Default tax category
    // Default warehouse
}