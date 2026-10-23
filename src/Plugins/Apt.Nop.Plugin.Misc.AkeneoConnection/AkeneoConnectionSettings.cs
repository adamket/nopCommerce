using Nop.Core.Configuration;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection;

/// <summary>
/// Connection credentials and the default profile. Everything a sync reads
/// values with (channel, locale, currency, unmapped-attribute behavior) lives
/// on the sync profile.
/// </summary>
public class AkeneoConnectionSettings : ISettings
{
    public string AkeneoConnectionClientId { get; set; }
    public string AkeneoConnectionClientSecret { get; set; }
    public string AkeneoConnectionBaseUrl { get; set; }
    public string AkeneoConnectionUsername { get; set; }
    public string AkeneoConnectionPassword { get; set; }
    public int? DefaultSyncProfileId { get; set; }
}
