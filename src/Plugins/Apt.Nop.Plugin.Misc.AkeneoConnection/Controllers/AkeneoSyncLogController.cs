using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Microsoft.AspNetCore.Mvc;
using Nop.Services.Security;
using Nop.Web.Areas.Admin.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Controllers;

public class AkeneoSyncLogController(
    IAkeneoSyncItemLogService akeneoSyncItemLogService,
    IAkeneoSyncRunRecordService syncRunRecordService) : BaseAdminController
{
    private const int RunsPageSize = 15;
    private const int ItemsPageSize = 20;

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

        return Json(new
        {
            success = true,
            runs = result.Select(r => new
            {
                id = r.Id,
                syncRunRecordId = r.Id,
                syncType = GetSyncTypeName(r.SyncTypeId),
                status = GetStatusName(r.SyncStatusId),
                startedOnUtc = r.StartedOnUtc.ToString("u"),
                finishedOnUtc = r.FinishedOnUtc?.ToString("u") ?? "",
                totalRead = r.TotalRead,
                createdCount = r.CreatedCount,
                updatedCount = r.UpdatedCount,
                skippedCount = r.SkippedCount,
                failedCount = r.FailedCount,
                errorSummary = r.ErrorSummary ?? ""
            }).ToList(),
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

        return Json(new
        {
            success = true,
            items = result.Select(item => new
            {
                id = item.Id,
                createdOnUtc = item.CreatedOnUtc.ToString("u"),
                action = GetActionName(item.ActionTypeId),
                akeneoIdentifier = item.AkeneoIdentifier ?? string.Empty,
                akeneoProductUuid = item.AkeneoProductUuid ?? string.Empty,
                nopProductId = item.NopProductId,
                message = item.Message ?? string.Empty
            }).ToList(),
            pageIndex = result.PageIndex,
            totalPages = result.TotalPages,
            totalCount = result.TotalCount
        });
    }

    private static string GetSyncTypeName(int id) =>
        Enum.IsDefined(typeof(SyncType), id) ? ((SyncType)id).ToString() : $"Type {id}";

    private static string GetStatusName(int id) =>
        Enum.IsDefined(typeof(SyncStatus), id) ? ((SyncStatus)id).ToString() : $"Status {id}";

    private static string GetActionName(int id) =>
        Enum.IsDefined(typeof(SyncItemActionType), id) ? ((SyncItemActionType)id).ToString() : $"Action {id}";
}