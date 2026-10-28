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

    /// <summary>
    /// Emails alerts about scheduled syncs. Manual runs never send alerts.
    /// </summary>
    public bool SyncAlertsEnabled { get; set; }

    /// <summary>
    /// Alert recipients, separated by commas or semicolons.
    /// </summary>
    public string SyncAlertEmails { get; set; }

    /// <summary>
    /// Failed products a scheduled run needs before it alerts. Runs that
    /// fail or are cancelled always alert.
    /// </summary>
    public int SyncAlertMinimumFailedProducts { get; set; } = 1;

    /// <summary>
    /// Hours without a finished scheduled sync before the stale-sync check
    /// alerts; 0 turns the check off.
    /// </summary>
    public int SyncAlertStaleAfterHours { get; set; } = 26;

    /// <summary>
    /// When the stale-sync check last sent an alert, so it repeats once per
    /// <see cref="SyncAlertStaleAfterHours"/> instead of every hour.
    /// </summary>
    public DateTime? SyncAlertLastStaleNotificationUtc { get; set; }

    /// <summary>
    /// Last save of a mapping that shapes product output (attribute, asset,
    /// family and category mappings). A delta run only revisits products
    /// changed in Akeneo, so the profile page compares this with its last resync.
    /// </summary>
    public DateTime? MappingsChangedOnUtc { get; set; }

    /// <summary>
    /// Akeneo locales (comma-separated, e.g. "es_US") that category content is
    /// translated into. Each writes the nopCommerce language with the matching
    /// culture; empty syncs category content in the standard language only.
    /// </summary>
    public string CategoryContentTranslationLocales { get; set; }
}
