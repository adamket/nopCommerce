using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Categories;
using Microsoft.AspNetCore.Mvc;
using Nop.Services.Localization;
using Nop.Services.Security;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Controllers;

/// <summary>
/// Category content mappings and runs, edited and started from the Category
/// Mapping page.
/// </summary>
[AuthorizeAdmin]
[Area(AreaNames.ADMIN)]
[AutoValidateAntiforgeryToken]
public class AkeneoCategoryContentController(
    IAkeneoCategoryContentMappingService contentMappingService,
    IAkeneoCategoryContentSyncService contentSyncService,
    ILanguageService languageService)
    : BasePluginController
{
    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    [HttpPost]
    public async Task<IActionResult> SaveMapping(AkeneoCategoryContentMappingModel model)
    {
        var existing = await contentMappingService.GetAllMappingsAsync();
        var mapping = model.Id > 0
            ? existing.FirstOrDefault(item => item.Id == model.Id)
            : new AkeneoCategoryContentMapping();

        if (mapping == null)
            return Json(new { success = false, errors = new[] { "The mapping could not be found. Reload the page." } });

        var isLabel = model.SourceTypeId == (int)AkeneoCategoryContentSourceType.Label;
        mapping.TargetId = model.TargetId;
        mapping.LanguageId = model.LanguageId;
        mapping.SourceTypeId = model.SourceTypeId;
        mapping.AkeneoAttributeCode = isLabel ? null : model.AkeneoAttributeCode.TrimOrNull();
        mapping.Locale = model.Locale.TrimOrNull();
        mapping.Channel = isLabel ? null : model.Channel.TrimOrNull();
        mapping.EmptyValueHandlingId = model.EmptyValueHandlingId;

        var errors = AkeneoCategoryContentPlanner.Validate(mapping, existing);
        if (mapping.LanguageId > 0 && await languageService.GetLanguageByIdAsync(mapping.LanguageId) == null)
            errors.Add("The selected language no longer exists.");

        if (errors.Count > 0)
            return Json(new { success = false, errors });

        if (mapping.Id > 0)
            await contentMappingService.UpdateMappingAsync(mapping);
        else
            await contentMappingService.InsertMappingAsync(mapping);

        return Json(new { success = true, id = mapping.Id });
    }

    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    [HttpPost]
    public async Task<IActionResult> SaveTranslationLocales(string locales)
    {
        var parsed = AkeneoCategoryContentPlanner.ParseLocales(locales);
        await contentMappingService.SaveTranslationLocalesAsync(parsed);

        return Json(new { success = true, locales = parsed });
    }

    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    [HttpPost]
    public async Task<IActionResult> DeleteMapping(int id)
    {
        var mapping = await contentMappingService.GetMappingByIdAsync(id);
        if (mapping != null)
            await contentMappingService.DeleteMappingAsync(mapping);

        return Json(new { success = true });
    }

    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    [HttpPost]
    public async Task<IActionResult> Preview(string akeneoCode = null)
    {
        var result = await contentSyncService.PreviewAsync(akeneoCode, HttpContext.RequestAborted);
        return Json(ToJson(result));
    }

    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    [HttpPost]
    public async Task<IActionResult> Sync(string akeneoCode = null)
    {
        var result = await contentSyncService.SyncAsync(akeneoCode, HttpContext.RequestAborted);
        return Json(ToJson(result));
    }

    private static object ToJson(AkeneoCategoryContentRunResult result) => new
    {
        success = result.Success,
        applied = result.Applied,
        alreadyRunning = result.AlreadyRunning,
        message = result.Message ?? string.Empty,
        syncRunRecordId = result.SyncRunRecordId,
        updatedCount = result.UpdatedCount,
        unchangedCount = result.UnchangedCount,
        skippedCount = result.SkippedCount,
        failedCount = result.FailedCount,
        categories = result.Categories.Select(item => new
        {
            akeneoCode = item.AkeneoCode,
            akeneoLabel = item.AkeneoLabel ?? item.AkeneoCode,
            nopCategoryId = item.NopCategoryId,
            nopCategoryName = item.NopCategoryName ?? string.Empty,
            statusId = (int)item.Status,
            status = item.Status.ToString(),
            changes = item.Changes.Select(change => new
            {
                property = change.Property,
                language = change.Language,
                from = change.From,
                to = change.To
            }),
            errors = item.Errors,
            warnings = item.Warnings
        })
    };}
