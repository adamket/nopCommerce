using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Categories;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

/// <summary>
/// Emails the configured recipients about scheduled syncs. Alerts are
/// best-effort: a failure to send is logged and never fails the sync.
/// </summary>
public interface IAkeneoSyncAlertService
{
    /// <summary>
    /// Alerts when a scheduled run failed, was cancelled, could not start, or
    /// had at least the configured number of failed products.
    /// </summary>
    Task NotifyScheduledRunAsync(AkeneoProductSyncExecutionResult result);

    /// <summary>
    /// Alerts when a scheduled run threw before it could report a result.
    /// </summary>
    Task NotifyScheduledRunErrorAsync(Exception exception);

    /// <summary>
    /// Alerts when a scheduled category content run failed, could not start, or
    /// had any failed category, such as one whose URL slug is already taken.
    /// </summary>
    Task NotifyScheduledCategoryContentRunAsync(AkeneoCategoryContentRunResult result);

    /// <summary>
    /// Alerts when no scheduled sync of the default profile has finished
    /// within the configured number of hours.
    /// </summary>
    Task CheckForStaleSyncAsync(DateTime utcNow);
}
