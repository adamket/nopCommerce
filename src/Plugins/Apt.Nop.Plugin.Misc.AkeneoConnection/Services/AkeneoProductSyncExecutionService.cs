using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Factories;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Humanizer;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public class AkeneoProductSyncExecutionService(
    IAkeneoProductBatchSyncService productSyncService,
    IAkeneoSyncRunRecordService syncRunRecordService,
    IAkeneoProductBatchImportRequestFactory requestFactory,
    IAkeneoSyncProfileService syncProfileService,
    IAkeneoSyncLeaseService syncLeaseService)
    : IAkeneoProductSyncExecutionService
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromHours(6);

    public async Task<AkeneoProductSyncExecutionResult> ImportProductsByProfileAsync(
        int profileId,
        SyncType syncType,
        CancellationToken cancellationToken = default)
    {
        var validation = await ValidateProfileAsync(profileId);
        if (validation.Result != null)
            return validation.Result;

        var profile = validation.Profile;
        var lease = await syncLeaseService.TryAcquireAsync(
            BuildLockKey(profile.Id),
            LeaseDuration);

        if (lease == null)
        {
            var activeRun = await syncRunRecordService.GetActiveRunAsync(null);
            return BuildAlreadyRunningResult(profile.Id, activeRun?.Id);
        }

        try
        {
            var runMode = ResolveRunMode(syncType);
            var scopeHash = AkeneoSyncScopeHasher.Build(profile);
            DateTime? previousWatermarkUtc = null;

            if (runMode == AkeneoRunMode.Delta &&
                (AkeneoUpdatedFilterMode)profile.UpdatedFilterModeId ==
                AkeneoUpdatedFilterMode.SinceLastSuccessfulRun)
            {
                var previousRun = await syncRunRecordService.GetLastSuccessfulRunAsync(
                    profile.Id,
                    scopeHash);

                previousWatermarkUtc = previousRun?.WatermarkUtc;
            }

            var runRecord = CreateRunRecord(
                profile.Id,
                syncType,
                runMode,
                scopeHash,
                JsonSerializer.Serialize(profile).Truncate(4000));

            await syncRunRecordService.InsertAkeneoSyncRunRecordAsync(runRecord);
            await syncLeaseService.SetRunRecordAsync(lease, runRecord.Id);

            var request = requestFactory.CreateFromProfile(
                profile,
                runRecord.Id,
                previousWatermarkUtc,
                runMode);

            request.SyncLeaseId = lease.Id;
            runRecord.SearchJsonSnapshot = request.SearchJson?.Truncate(4000);
            await syncRunRecordService.UpdateAkeneoSyncRunRecordAsync(runRecord);

            return await SyncProductsCoreAsync(
                request,
                runRecord,
                profile.Id,
                cancellationToken);
        }
        finally
        {
            await syncLeaseService.ReleaseAsync(lease);
        }
    }

    public async Task<AkeneoProductSyncExecutionResult> SyncProductByUuidAsync(
        int profileId,
        string akeneoProductUuid,
        CancellationToken cancellationToken = default)
    {
        var validation = await ValidateProfileAsync(profileId);
        if (validation.Result != null)
            return validation.Result;

        if (string.IsNullOrWhiteSpace(akeneoProductUuid))
        {
            return new AkeneoProductSyncExecutionResult
            {
                Success = false,
                ProfileId = profileId,
                SyncStatus = SyncStatus.Failed,
                Message = "Akeneo product UUID is required.",
                Errors = new List<string> { "Akeneo product UUID is required." }
            };
        }

        var profile = validation.Profile;
        var lease = await syncLeaseService.TryAcquireAsync(
            BuildLockKey(profile.Id),
            LeaseDuration);

        if (lease == null)
        {
            var activeRun = await syncRunRecordService.GetActiveRunAsync(null);
            return BuildAlreadyRunningResult(profile.Id, activeRun?.Id);
        }

        try
        {
            var scopeHash = AkeneoSyncScopeHasher.Build(profile);
            var runRecord = CreateRunRecord(
                profile.Id,
                SyncType.ManualProductSync,
                AkeneoRunMode.SingleProduct,
                scopeHash,
                JsonSerializer.Serialize(profile).Truncate(4000));

            await syncRunRecordService.InsertAkeneoSyncRunRecordAsync(runRecord);
            await syncLeaseService.SetRunRecordAsync(lease, runRecord.Id);

            var request = requestFactory.CreateFromProfile(
                profile,
                runRecord.Id,
                runMode: AkeneoRunMode.SingleProduct);

            request.SyncLeaseId = lease.Id;
            request.AkeneoProductUuid = akeneoProductUuid.Trim();

            try
            {
                var itemResult = await productSyncService.SyncProductByUuidAsync(
                    request,
                    cancellationToken);

                ApplyItemResultToRunRecord(runRecord, itemResult);
                await syncRunRecordService.UpdateAkeneoSyncRunRecordAsync(runRecord);

                return new AkeneoProductSyncExecutionResult
                {
                    Success = itemResult.Success,
                    CompletedWithErrors = !itemResult.Success,
                    ProfileId = profile.Id,
                    SyncRunRecordId = runRecord.Id,
                    SyncStatus = (SyncStatus)runRecord.SyncStatusId,
                    Message = itemResult.Success
                        ? $"Akeneo product synchronization {itemResult.ActionType.ToString().ToLowerInvariant()}."
                        : "Akeneo product synchronization failed.",
                    TotalRead = 1,
                    CreatedCount = itemResult.ActionType == SyncItemActionType.Created ? 1 : 0,
                    UpdatedCount = itemResult.ActionType == SyncItemActionType.Updated ? 1 : 0,
                    SkippedCount = itemResult.ActionType == SyncItemActionType.Skipped ? 1 : 0,
                    FailedCount = itemResult.Success ? 0 : 1,
                    WarningCount = itemResult.Warnings.Any() ? 1 : 0,
                    ItemActionType = itemResult.ActionType,
                    NopProductId = itemResult.NopProductId,
                    AkeneoProductUuid = itemResult.AkeneoProductUuid,
                    AkeneoIdentifier = itemResult.AkeneoIdentifier,
                    Errors = itemResult.Errors.ToList(),
                    Warnings = itemResult.Warnings.ToList(),
                    Messages = itemResult.Messages.ToList()
                };
            }
            catch (OperationCanceledException)
            {
                runRecord.FinishedOnUtc = DateTime.UtcNow;
                runRecord.SyncStatusId = (int)SyncStatus.Cancelled;
                runRecord.ErrorSummary = "Product synchronization was canceled.";
                await syncRunRecordService.UpdateAkeneoSyncRunRecordAsync(runRecord);

                return new AkeneoProductSyncExecutionResult
                {
                    Success = false,
                    Canceled = true,
                    ProfileId = profile.Id,
                    SyncRunRecordId = runRecord.Id,
                    SyncStatus = SyncStatus.Cancelled,
                    Message = "Akeneo product synchronization was canceled."
                };
            }
            catch (Exception ex)
            {
                runRecord.FinishedOnUtc = DateTime.UtcNow;
                runRecord.SyncStatusId = (int)SyncStatus.Failed;
                runRecord.ErrorSummary = ex.Message.Truncate(4000);
                await syncRunRecordService.UpdateAkeneoSyncRunRecordAsync(runRecord);

                return new AkeneoProductSyncExecutionResult
                {
                    Success = false,
                    ProfileId = profile.Id,
                    SyncRunRecordId = runRecord.Id,
                    SyncStatus = SyncStatus.Failed,
                    Message = $"Akeneo product synchronization failed: {ex.Message}",
                    Errors = new List<string> { ex.Message }
                };
            }
        }
        finally
        {
            await syncLeaseService.ReleaseAsync(lease);
        }
    }

    public async Task<AkeneoProductSyncExecutionResult> SyncProductModelByCodeAsync(
        int profileId,
        string akeneoProductModelCode,
        CancellationToken cancellationToken = default)
    {
        var validation = await ValidateProfileAsync(profileId);
        if (validation.Result != null)
            return validation.Result;

        if (string.IsNullOrWhiteSpace(akeneoProductModelCode))
        {
            return new AkeneoProductSyncExecutionResult
            {
                Success = false,
                ProfileId = profileId,
                SyncStatus = SyncStatus.Failed,
                Message = "Akeneo product model code is required.",
                Errors = new List<string> { "Akeneo product model code is required." }
            };
        }

        var profile = validation.Profile;
        var lease = await syncLeaseService.TryAcquireAsync(
            BuildLockKey(profile.Id),
            LeaseDuration);

        if (lease == null)
        {
            var activeRun = await syncRunRecordService.GetActiveRunAsync(null);
            return BuildAlreadyRunningResult(profile.Id, activeRun?.Id);
        }

        try
        {
            var scopeHash = AkeneoSyncScopeHasher.Build(profile);
            var runRecord = CreateRunRecord(
                profile.Id,
                SyncType.ManualProductSync,
                AkeneoRunMode.SingleProduct,
                scopeHash,
                JsonSerializer.Serialize(profile).Truncate(4000));

            await syncRunRecordService.InsertAkeneoSyncRunRecordAsync(runRecord);
            await syncLeaseService.SetRunRecordAsync(lease, runRecord.Id);

            var request = requestFactory.CreateFromProfile(
                profile,
                runRecord.Id,
                runMode: AkeneoRunMode.SingleProduct);

            request.SyncLeaseId = lease.Id;
            request.AkeneoProductModelCode = akeneoProductModelCode.Trim();

            try
            {
                var itemResult = await productSyncService.SyncProductModelByCodeAsync(
                    request,
                    cancellationToken);

                ApplyItemResultToRunRecord(runRecord, itemResult);
                await syncRunRecordService.UpdateAkeneoSyncRunRecordAsync(runRecord);

                return new AkeneoProductSyncExecutionResult
                {
                    Success = itemResult.Success,
                    CompletedWithErrors = !itemResult.Success,
                    ProfileId = profile.Id,
                    SyncRunRecordId = runRecord.Id,
                    SyncStatus = (SyncStatus)runRecord.SyncStatusId,
                    Message = itemResult.Success
                        ? $"Akeneo product model synchronization {itemResult.ActionType.ToString().ToLowerInvariant()}."
                        : "Akeneo product model synchronization failed.",
                    TotalRead = 1,
                    CreatedCount = itemResult.ActionType == SyncItemActionType.Created ? 1 : 0,
                    UpdatedCount = itemResult.ActionType == SyncItemActionType.Updated ? 1 : 0,
                    SkippedCount = itemResult.ActionType == SyncItemActionType.Skipped ? 1 : 0,
                    FailedCount = itemResult.Success ? 0 : 1,
                    WarningCount = itemResult.Warnings.Any() ? 1 : 0,
                    ItemActionType = itemResult.ActionType,
                    NopProductId = itemResult.NopProductId,
                    AkeneoIdentifier = itemResult.AkeneoIdentifier,
                    Errors = itemResult.Errors.ToList(),
                    Warnings = itemResult.Warnings.ToList(),
                    Messages = itemResult.Messages.ToList()
                };
            }
            catch (OperationCanceledException)
            {
                runRecord.FinishedOnUtc = DateTime.UtcNow;
                runRecord.SyncStatusId = (int)SyncStatus.Cancelled;
                runRecord.ErrorSummary = "Product model synchronization was canceled.";
                await syncRunRecordService.UpdateAkeneoSyncRunRecordAsync(runRecord);

                return new AkeneoProductSyncExecutionResult
                {
                    Success = false,
                    Canceled = true,
                    ProfileId = profile.Id,
                    SyncRunRecordId = runRecord.Id,
                    SyncStatus = SyncStatus.Cancelled,
                    Message = "Akeneo product model synchronization was canceled."
                };
            }
            catch (Exception ex)
            {
                runRecord.FinishedOnUtc = DateTime.UtcNow;
                runRecord.SyncStatusId = (int)SyncStatus.Failed;
                runRecord.ErrorSummary = ex.Message.Truncate(4000);
                await syncRunRecordService.UpdateAkeneoSyncRunRecordAsync(runRecord);

                return new AkeneoProductSyncExecutionResult
                {
                    Success = false,
                    ProfileId = profile.Id,
                    SyncRunRecordId = runRecord.Id,
                    SyncStatus = SyncStatus.Failed,
                    Message = $"Akeneo product model synchronization failed: {ex.Message}",
                    Errors = new List<string> { ex.Message }
                };
            }
        }
        finally
        {
            await syncLeaseService.ReleaseAsync(lease);
        }
    }

    public async Task<AkeneoProductSyncExecutionResult> ImportProductsAsync(
        AkeneoProductBatchImportRequest request,
        SyncType syncType,
        int? profileId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        profileId ??= request.SyncProfileId;
        request.SyncProfileId = profileId;
        request.RunMode = ResolveRunMode(syncType);

        var lease = await syncLeaseService.TryAcquireAsync(
            BuildLockKey(profileId),
            LeaseDuration);

        if (lease == null)
        {
            var activeRun = await syncRunRecordService.GetActiveRunAsync(null);
            return BuildAlreadyRunningResult(profileId, activeRun?.Id);
        }

        try
        {
            var runRecord = CreateRunRecord(
                profileId,
                syncType,
                request.RunMode,
                request.ScopeHash,
                null);

            runRecord.SearchJsonSnapshot = request.SearchJson?.Truncate(4000);

            await syncRunRecordService.InsertAkeneoSyncRunRecordAsync(runRecord);
            await syncLeaseService.SetRunRecordAsync(lease, runRecord.Id);

            request.SyncRunRecordId = runRecord.Id;
            request.SyncLeaseId = lease.Id;

            return await SyncProductsCoreAsync(
                request,
                runRecord,
                profileId,
                cancellationToken);
        }
        finally
        {
            await syncLeaseService.ReleaseAsync(lease);
        }
    }

    private async Task<AkeneoProductSyncExecutionResult> SyncProductsCoreAsync(
        AkeneoProductBatchImportRequest request,
        AkeneoSyncRunRecord runRecord,
        int? profileId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await productSyncService.SyncProductsAsync(
                request,
                cancellationToken);

            ApplyResultToRunRecord(runRecord, result);
            await syncRunRecordService.UpdateAkeneoSyncRunRecordAsync(runRecord);

            return BuildExecutionResult(runRecord, result, profileId);
        }
        catch (OperationCanceledException)
        {
            runRecord.FinishedOnUtc = DateTime.UtcNow;
            runRecord.SyncStatusId = (int)SyncStatus.Cancelled;
            runRecord.ErrorSummary = "Product synchronization was canceled.";
            await syncRunRecordService.UpdateAkeneoSyncRunRecordAsync(runRecord);

            return new AkeneoProductSyncExecutionResult
            {
                Success = false,
                Canceled = true,
                ProfileId = profileId,
                SyncRunRecordId = runRecord.Id,
                SyncStatus = SyncStatus.Cancelled,
                Message = "Akeneo product synchronization was canceled."
            };
        }
        catch (Exception ex)
        {
            runRecord.FinishedOnUtc = DateTime.UtcNow;
            runRecord.SyncStatusId = (int)SyncStatus.Failed;
            runRecord.ErrorSummary = ex.Message.Truncate(4000);
            await syncRunRecordService.UpdateAkeneoSyncRunRecordAsync(runRecord);

            return new AkeneoProductSyncExecutionResult
            {
                Success = false,
                ProfileId = profileId,
                SyncRunRecordId = runRecord.Id,
                SyncStatus = SyncStatus.Failed,
                Message = $"Akeneo product synchronization failed: {ex.Message}",
                Errors = new List<string> { ex.Message }
            };
        }
    }

    private async Task<(AkeneoSyncProfile Profile, AkeneoProductSyncExecutionResult Result)>
        ValidateProfileAsync(int profileId)
    {
        var profile = await syncProfileService.GetAkeneoSyncProfileByIdAsync(profileId);

        if (profile == null)
        {
            return (null, new AkeneoProductSyncExecutionResult
            {
                Success = false,
                ProfileNotFound = true,
                ProfileId = profileId,
                SyncStatus = SyncStatus.Failed,
                Message = "Akeneo sync profile was not found."
            });
        }

        if (!profile.Enabled)
        {
            return (profile, new AkeneoProductSyncExecutionResult
            {
                Success = false,
                ProfileDisabled = true,
                ProfileId = profile.Id,
                SyncStatus = SyncStatus.Failed,
                Message = "This Akeneo sync profile is disabled."
            });
        }

        return (profile, null);
    }

    private static AkeneoSyncRunRecord CreateRunRecord(
        int? profileId,
        SyncType syncType,
        AkeneoRunMode runMode,
        string scopeHash,
        string profileSnapshotJson)
    {
        var startedOnUtc = DateTime.UtcNow;

        return new AkeneoSyncRunRecord
        {
            SyncProfileId = profileId,
            SyncTypeId = (int)syncType,
            RunModeId = (int)runMode,
            StartedOnUtc = startedOnUtc,
            WatermarkUtc = startedOnUtc,
            SyncStatusId = (int)SyncStatus.Started,
            ScopeHash = scopeHash,
            ProfileSnapshotJson = profileSnapshotJson
        };
    }

    private static void ApplyItemResultToRunRecord(
        AkeneoSyncRunRecord runRecord,
        AkeneoProductImportResult result)
    {
        runRecord.FinishedOnUtc = DateTime.UtcNow;
        runRecord.TotalRead = 1;
        runRecord.CreatedCount = result.ActionType == SyncItemActionType.Created ? 1 : 0;
        runRecord.UpdatedCount = result.ActionType == SyncItemActionType.Updated ? 1 : 0;
        runRecord.SkippedCount = result.ActionType == SyncItemActionType.Skipped ? 1 : 0;
        runRecord.FailedCount = result.Success ? 0 : 1;
        runRecord.WarningCount = result.Warnings.Any() ? 1 : 0;
        runRecord.CompletedAllPages = true;
        runRecord.WasTruncated = false;
        runRecord.ReconciliationCompleted = false;
        runRecord.SyncStatusId = !result.Success
            ? (int)SyncStatus.CompletedWithErrors
            : result.Warnings.Any()
                ? (int)SyncStatus.CompletedWithWarnings
                : (int)SyncStatus.Completed;
        runRecord.ErrorSummary = result.Errors.Any()
            ? string.Join(Environment.NewLine, result.Errors).Truncate(4000)
            : null;
    }

    private static void ApplyResultToRunRecord(
        AkeneoSyncRunRecord runRecord,
        AkeneoProductBatchImportResult result)
    {
        runRecord.FinishedOnUtc = DateTime.UtcNow;
        runRecord.TotalRead = result.TotalRead;
        runRecord.CreatedCount = result.CreatedCount;
        runRecord.UpdatedCount = result.UpdatedCount;
        runRecord.SkippedCount = result.SkippedCount;
        runRecord.FailedCount = result.FailedCount;
        runRecord.WarningCount = result.WarningCount;
        runRecord.CompletedAllPages = result.CompletedAllPages;
        runRecord.WasTruncated = result.WasTruncated;
        runRecord.ReconciliationCompleted = result.ReconciliationCompleted;
        runRecord.SyncStatusId = (int)result.SyncStatus;
        runRecord.ErrorSummary = result.Errors.Any()
            ? string.Join(Environment.NewLine, result.Errors).Truncate(4000)
            : null;
    }

    private static AkeneoProductSyncExecutionResult BuildExecutionResult(
        AkeneoSyncRunRecord runRecord,
        AkeneoProductBatchImportResult result,
        int? profileId)
    {
        return new AkeneoProductSyncExecutionResult
        {
            Success = result.Success,
            Canceled = result.Canceled,
            CompletedWithErrors = result.Errors.Any() || result.FailedCount > 0,
            ProfileId = profileId,
            SyncRunRecordId = runRecord.Id,
            SyncStatus = result.SyncStatus,
            Message = BuildSyncMessage(result),
            TotalRead = result.TotalRead,
            CreatedCount = result.CreatedCount,
            UpdatedCount = result.UpdatedCount,
            SkippedCount = result.SkippedCount,
            FailedCount = result.FailedCount,
            WarningCount = result.WarningCount,
            ReconciledCount = result.ReconciledCount,
            Errors = result.Errors.ToList(),
            Warnings = result.LoggedItemResults
                .SelectMany(item => item.Warnings)
                .Distinct(StringComparer.Ordinal)
                .Take(100)
                .ToList(),
            Messages = result.Messages.ToList()
        };
    }

    private static string BuildSyncMessage(AkeneoProductBatchImportResult result)
    {
        if (result.Canceled)
            return "Akeneo product synchronization was canceled.";

        var message =
            $"Akeneo product synchronization completed. Read: {result.TotalRead}, " +
            $"Created: {result.CreatedCount}, Updated: {result.UpdatedCount}, " +
            $"Skipped: {result.SkippedCount}, Failed: {result.FailedCount}, " +
            $"Reconciled: {result.ReconciledCount}.";

        if (result.Errors.Any() || result.FailedCount > 0)
            return "Akeneo product synchronization completed with errors. " + message;

        if (result.WarningCount > 0)
            return "Akeneo product synchronization completed with warnings. " + message;

        return message;
    }

    private static AkeneoRunMode ResolveRunMode(SyncType syncType) => syncType switch
    {
        SyncType.InitialImport => AkeneoRunMode.Full,
        SyncType.DeltaSync => AkeneoRunMode.Delta,
        SyncType.ManualProductSync => AkeneoRunMode.SingleProduct,
        SyncType.ManualProfileSync => AkeneoRunMode.Delta,
        SyncType.ManualFullProfileSync => AkeneoRunMode.Full,
        SyncType.ScheduledFullSync => AkeneoRunMode.Full,
        _ => AkeneoRunMode.Delta
    };

    // Profiles may overlap until an explicit ownership/priority policy is
    // configured, so serialize catalog writers by default.
    private static string BuildLockKey(int? profileId) =>
        "akeneo-catalog-writer";

    private static AkeneoProductSyncExecutionResult BuildAlreadyRunningResult(
        int? profileId,
        int? activeRunRecordId)
    {
        var suffix = activeRunRecordId.HasValue
            ? $" (run record {activeRunRecordId.Value})"
            : string.Empty;

        return new AkeneoProductSyncExecutionResult
        {
            Success = false,
            AlreadyRunning = true,
            ProfileId = profileId,
            SyncRunRecordId = activeRunRecordId,
            SyncStatus = SyncStatus.Started,
            Message = $"An Akeneo sync for this scope is already running{suffix}."
        };
    }
}
