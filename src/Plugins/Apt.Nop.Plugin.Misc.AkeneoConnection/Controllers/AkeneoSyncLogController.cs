using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Microsoft.AspNetCore.Mvc;
using Nop.Services.Helpers;
using Nop.Services.Security;
using Nop.Web.Areas.Admin.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Controllers;

public class AkeneoSyncLogController(
    IAkeneoSyncItemLogService akeneoSyncItemLogService,
    IAkeneoSyncRunRecordService syncRunRecordService,
    IDateTimeHelper dateTimeHelper) : BaseAdminController
{
    private const int RunsPageSize = 15;
    private const int ItemsPageSize = 20;

    [HttpGet("admin/akeneo-connection/sync-logs/list")]
    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    public IActionResult List()
    {
        var model = new AkeneoSyncRunListModel
        {
            SyncRunRecordPageSize = RunsPageSize,
            SyncItemLogPageSize = ItemsPageSize
        };

        return View(
            "~/Plugins/Apt.Misc.AkeneoConnection/Views/AkeneoSyncLogs.cshtml",
            model);
    }

    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    [HttpPost]
    public async Task<IActionResult> Runs(int page = 0, int pageSize = 0)
    {
        if (pageSize <= 0)
            pageSize = RunsPageSize;

        var result = await syncRunRecordService.SearchAkeneoSyncRunRecordsAsync(
            pageIndex: page,
            pageSize: pageSize);

        var runs = new List<object>();
        foreach (var run in result)
        {
            runs.Add(new
            {
                id = run.Id,
                syncRunRecordId = run.Id,
                syncType = GetSyncTypeName(run.SyncTypeId),
                status = GetStatusName(run.SyncStatusId),
                startedOn = await FormatUserTimeAsync(run.StartedOnUtc),
                finishedOn = run.FinishedOnUtc.HasValue
                    ? await FormatUserTimeAsync(run.FinishedOnUtc.Value) : "",
                totalRead = run.TotalRead,
                createdCount = run.CreatedCount,
                updatedCount = run.UpdatedCount,
                skippedCount = run.SkippedCount,
                failedCount = run.FailedCount,
                errorSummary = run.ErrorSummary ?? ""
            });
        }

        return Json(new
        {
            success = true,
            runs,
            pageIndex = result.PageIndex,
            totalPages = result.TotalPages,
            totalCount = result.TotalCount
        });
    }

    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    [HttpPost]
    public async Task<IActionResult> RunItems(int syncRunRecordId, int page = 0, int pageSize = 0)
    {
        if (pageSize <= 0)
            pageSize = ItemsPageSize;

        var result = await akeneoSyncItemLogService.SearchAkeneoSyncItemLogsAsync(
            syncRunRecordId: syncRunRecordId,
            pageIndex: page,
            pageSize: pageSize);

        var items = new List<object>();
        foreach (var item in result)
        {
            items.Add(new
            {
                id = item.Id,
                createdOn = await FormatUserTimeAsync(item.CreatedOnUtc),
                action = GetActionName(item.ActionTypeId),
                akeneoIdentifier = item.AkeneoIdentifier ?? string.Empty,
                akeneoProductUuid = item.AkeneoProductUuid ?? string.Empty,
                nopProductId = item.NopProductId,
                message = item.Message ?? string.Empty
            });
        }

        return Json(new
        {
            success = true,
            items,
            pageIndex = result.PageIndex,
            totalPages = result.TotalPages,
            totalCount = result.TotalCount
        });
    }

    private async Task<string> FormatUserTimeAsync(DateTime utc) =>
        (await dateTimeHelper.ConvertToUserTimeAsync(
            DateTime.SpecifyKind(utc, DateTimeKind.Utc),
            DateTimeKind.Utc)).ToString("yyyy-MM-dd HH:mm:ss");

    private static string GetSyncTypeName(int id) =>
        Enum.IsDefined(typeof(SyncType), id) ? ((SyncType)id).ToString() : $"Type {id}";

    private static string GetStatusName(int id) =>
        Enum.IsDefined(typeof(SyncStatus), id) ? ((SyncStatus)id).ToString() : $"Status {id}";

    private static string GetActionName(int id) =>
        Enum.IsDefined(typeof(SyncItemActionType), id) ? ((SyncItemActionType)id).ToString() : $"Action {id}";
}
