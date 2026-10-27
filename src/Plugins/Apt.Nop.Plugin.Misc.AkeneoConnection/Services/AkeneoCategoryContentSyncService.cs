using System.Linq.Expressions;
using System.Text.RegularExpressions;
using System.Transactions;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Categories;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Seo;
using Nop.Data;
using Nop.Services.Catalog;
using Nop.Services.Localization;
using Nop.Services.Media;
using Nop.Services.Seo;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public interface IAkeneoCategoryContentSyncService
{
    /// <summary>
    /// Plans the category content sync without writing anything. With a code,
    /// only that Akeneo category is planned.
    /// </summary>
    Task<AkeneoCategoryContentRunResult> PreviewAsync(
        string akeneoCategoryCode = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the mapped Akeneo content into every mapped nopCommerce category,
    /// or only the one with the given code. A category with any problem is
    /// left unchanged.
    /// </summary>
    Task<AkeneoCategoryContentRunResult> SyncAsync(
        string akeneoCategoryCode = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Copies category template values and labels from Akeneo into the nopCommerce
/// categories they are mapped to. Akeneo owns every mapped property, so the
/// sync compares against what nopCommerce holds now and overwrites any edit.
/// </summary>
public class AkeneoCategoryContentSyncService(
    IAkeneoApiClient apiClient,
    IAkeneoCategoryContentMappingService contentMappingService,
    IAkeneoNopEntityMappingService entityMappingService,
    ICategoryService categoryService,
    ILanguageService languageService,
    ILocalizedEntityService localizedEntityService,
    IUrlRecordService urlRecordService,
    IPictureService pictureService,
    IAkeneoSyncLeaseService syncLeaseService,
    IAkeneoSyncRunRecordService syncRunRecordService,
    IAkeneoSyncItemLogService syncItemLogService,
    IProductService productService,
    IRepository<UrlRecord> urlRecordRepository,
    SeoSettings seoSettings)
    : IAkeneoCategoryContentSyncService
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(30);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);
    private const int DisplayLength = 160;

    // Name first, so a slug rebuilt from the name uses the name being written.
    private static readonly AkeneoCategoryContentTarget[] PlanningOrder =
    [
        AkeneoCategoryContentTarget.Name,
        AkeneoCategoryContentTarget.Description,
        AkeneoCategoryContentTarget.MetaTitle,
        AkeneoCategoryContentTarget.MetaDescription,
        AkeneoCategoryContentTarget.MetaKeywords,
        AkeneoCategoryContentTarget.SeName,
        AkeneoCategoryContentTarget.Picture
    ];

    #region Public

    public async Task<AkeneoCategoryContentRunResult> PreviewAsync(
        string akeneoCategoryCode = null,
        CancellationToken cancellationToken = default)
    {
        var result = new AkeneoCategoryContentRunResult();
        var context = await LoadAsync(akeneoCategoryCode, result, cancellationToken);
        if (context == null)
            return result;

        foreach (var (code, nopCategoryId) in context.Targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var plan = await PlanCategoryAsync(context, code, nopCategoryId);
            result.Categories.Add(plan.Result);
        }

        result.Message = Summarize(result);
        return result;
    }

    public async Task<AkeneoCategoryContentRunResult> SyncAsync(
        string akeneoCategoryCode = null,
        CancellationToken cancellationToken = default)
    {
        var result = new AkeneoCategoryContentRunResult { Applied = true };

        var lease = await syncLeaseService.TryAcquireAsync(
            AkeneoConnectionConstants.CatalogWriterLockKey,
            LeaseDuration);

        if (lease == null)
        {
            result.Success = false;
            result.AlreadyRunning = true;
            result.Message = "An Akeneo sync or binding run is already in progress. Try again when it finishes.";
            return result;
        }

        AkeneoSyncRunRecord runRecord = null;

        try
        {
            var context = await LoadAsync(akeneoCategoryCode, result, cancellationToken);
            if (context == null)
                return result;

            var startedOnUtc = DateTime.UtcNow;
            runRecord = new AkeneoSyncRunRecord
            {
                SyncTypeId = (int)SyncType.CategoryContent,
                RunModeId = (int)AkeneoRunMode.Full,
                SyncStatusId = (int)SyncStatus.Started,
                StartedOnUtc = startedOnUtc,
                WatermarkUtc = startedOnUtc,
                SearchJsonSnapshot = akeneoCategoryCode?.Trim()
            };
            await syncRunRecordService.InsertAkeneoSyncRunRecordAsync(runRecord);
            await syncLeaseService.SetRunRecordAsync(lease, runRecord.Id);
            result.SyncRunRecordId = runRecord.Id;

            foreach (var (code, nopCategoryId) in context.Targets)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var plan = await PlanCategoryAsync(context, code, nopCategoryId);

                if (plan.Result.Status == AkeneoCategoryContentStatus.Updated)
                {
                    try
                    {
                        await ApplyAsync(plan, cancellationToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        plan.Result.Status = AkeneoCategoryContentStatus.Failed;
                        plan.Result.Errors.Add($"The category could not be updated: {ex.Message}");

                        // The write rolled back; drop any picture ownership it recorded in memory.
                        plan.State = await contentMappingService.GetStateAsync(code);
                    }
                }

                if (plan.Category != null && plan.Result.Status != AkeneoCategoryContentStatus.Updated)
                    await SaveOutcomeAsync(plan);

                result.Categories.Add(plan.Result);
                await LogItemAsync(runRecord, plan.Result);
            }

            result.Message = Summarize(result);
            result.Success = result.FailedCount == 0;

            runRecord.SyncStatusId = (int)(result.FailedCount > 0
                ? SyncStatus.CompletedWithErrors
                : result.WarningCount > 0
                    ? SyncStatus.CompletedWithWarnings
                    : SyncStatus.Completed);
            Finish(runRecord, result);
            runRecord.CompletedAllPages = true;
            await syncRunRecordService.UpdateAkeneoSyncRunRecordAsync(runRecord);

            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"Category content sync failed: {ex.Message}";

            if (runRecord != null)
            {
                runRecord.SyncStatusId = (int)(ex is OperationCanceledException ? SyncStatus.Cancelled : SyncStatus.Failed);
                runRecord.ErrorSummary = CommonHelper.EnsureMaximumLength(ex.Message, 4000);
                Finish(runRecord, result);
                await syncRunRecordService.UpdateAkeneoSyncRunRecordAsync(runRecord);
            }

            if (ex is OperationCanceledException)
                throw;

            return result;
        }
        finally
        {
            await syncLeaseService.ReleaseAsync(lease);
        }
    }

    #endregion

    #region Loading

    private sealed class RunContext
    {
        public IList<AkeneoCategoryContentMapping> Mappings { get; init; }

        public IDictionary<int, string> LanguageNames { get; init; }

        public IDictionary<string, AkeneoCategoryDefinition> AkeneoCategories { get; init; }

        public IList<(string Code, int NopCategoryId)> Targets { get; init; }
    }

    /// <summary>Reads mappings, mapped categories and Akeneo; null (with a message) when there is nothing to do.</summary>
    private async Task<RunContext> LoadAsync(
        string akeneoCategoryCode,
        AkeneoCategoryContentRunResult result,
        CancellationToken cancellationToken)
    {
        var mappings = await contentMappingService.GetAllMappingsAsync();
        if (mappings.Count == 0)
        {
            result.Message = "No category content mappings are set up yet.";
            return null;
        }

        var code = akeneoCategoryCode?.Trim();
        var targets = (await entityMappingService.GetAkeneoNopEntityMappingsAsync(AkeneoEntityType.Category))
            .Where(mapping =>
                mapping.NopEntityTypeId == (int)NopEntityType.Category &&
                mapping.NopEntityId > 0 &&
                !string.IsNullOrWhiteSpace(mapping.AkeneoCode) &&
                (code == null || string.Equals(mapping.AkeneoCode.Trim(), code, StringComparison.OrdinalIgnoreCase)))
            .GroupBy(mapping => mapping.AkeneoCode.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => (Code: group.Key, NopCategoryId: group.First().NopEntityId))
            .OrderBy(target => target.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (targets.Count == 0)
        {
            result.Message = code == null
                ? "No Akeneo categories are mapped to nopCommerce categories yet."
                : $"Akeneo category '{code}' is not mapped to a nopCommerce category.";
            return null;
        }

        var akeneoCategories = (await apiClient.GetCategoriesWithValuesAsync(cancellationToken: cancellationToken))
            .Where(category => !string.IsNullOrWhiteSpace(category.Code))
            .GroupBy(category => category.Code.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var languageNames = (await languageService.GetAllLanguagesAsync(showHidden: true))
            .ToDictionary(language => language.Id, language => language.Name);

        return new RunContext
        {
            Mappings = mappings,
            LanguageNames = languageNames,
            AkeneoCategories = akeneoCategories,
            Targets = targets
        };
    }

    #endregion

    #region Planning

    private sealed class CategoryPlan
    {
        public AkeneoCategoryContentItemResult Result { get; init; }

        public Category Category { get; set; }

        public AkeneoCategoryContentState State { get; set; }

        public List<(AkeneoCategoryContentTarget Target, int LanguageId, string Value)> TextWrites { get; } = new();

        public List<(int LanguageId, string Slug)> SlugWrites { get; } = new();

        /// <summary>URL records of deleted entities, removed before the slugs are saved.</summary>
        public List<UrlRecord> ReleasedSlugRecords { get; } = new();

        /// <summary>Set when the picture changes; a null image clears it.</summary>
        public PictureWrite Picture { get; set; }

        /// <summary>Names being written, by language, for slugs rebuilt from the name.</summary>
        public Dictionary<int, string> PlannedNames { get; } = new();
    }

    private sealed record PictureWrite(AkeneoCategoryValue Image);

    private async Task<CategoryPlan> PlanCategoryAsync(
        RunContext context,
        string code,
        int nopCategoryId)
    {
        var plan = new CategoryPlan
        {
            Result = new AkeneoCategoryContentItemResult
            {
                AkeneoCode = code,
                AkeneoLabel = code,
                NopCategoryId = nopCategoryId
            }
        };
        var result = plan.Result;

        var category = await categoryService.GetCategoryByIdAsync(nopCategoryId);
        if (category == null || category.Deleted)
        {
            result.Status = AkeneoCategoryContentStatus.Skipped;
            result.Warnings.Add($"The mapped nopCommerce category (ID {nopCategoryId}) no longer exists.");
            return plan;
        }

        plan.Category = category;
        plan.State = await contentMappingService.GetStateAsync(code);
        result.NopCategoryName = category.Name;

        if (!context.AkeneoCategories.TryGetValue(code, out var akeneoCategory))
        {
            result.Status = AkeneoCategoryContentStatus.Skipped;
            result.Warnings.Add($"Akeneo category '{code}' no longer exists, so its content was not synced.");
            return plan;
        }

        result.AkeneoLabel = akeneoCategory.GetLabel();
        var values = AkeneoCategoryValueReader.Read(akeneoCategory.Values);

        var mappings = context.Mappings
            .OrderBy(mapping => Array.IndexOf(PlanningOrder, mapping.Target))
            .ThenBy(mapping => mapping.LanguageId);

        foreach (var mapping in mappings)
        {
            if (!Enum.IsDefined(mapping.Target))
                continue;

            if (mapping.LanguageId > 0 && !context.LanguageNames.ContainsKey(mapping.LanguageId))
            {
                result.Warnings.Add(
                    $"{AkeneoCategoryContentPlanner.DescribeTarget(mapping.Target)} is mapped for language ID {mapping.LanguageId}, which no longer exists; that mapping was skipped.");
                continue;
            }

            var source = AkeneoCategoryContentPlanner.ResolveSource(mapping, akeneoCategory, values);
            if (source.Problem != null)
            {
                result.Errors.Add(source.Problem);
                continue;
            }

            switch (mapping.Target)
            {
                case AkeneoCategoryContentTarget.Picture:
                    await PlanPictureAsync(plan, mapping, source);
                    break;
                case AkeneoCategoryContentTarget.SeName:
                    await PlanSlugAsync(plan, context, mapping, source);
                    break;
                default:
                    await PlanTextAsync(plan, context, mapping, source);
                    break;
            }
        }

        result.Status = result.Errors.Count > 0
            ? AkeneoCategoryContentStatus.Failed
            : result.Changes.Count > 0
                ? AkeneoCategoryContentStatus.Updated
                : AkeneoCategoryContentStatus.Unchanged;

        return plan;
    }

    private async Task PlanTextAsync(
        CategoryPlan plan,
        RunContext context,
        AkeneoCategoryContentMapping mapping,
        AkeneoCategoryContentSource source)
    {
        var current = await GetCurrentTextAsync(plan.Category, mapping.Target, mapping.LanguageId);
        var decision = AkeneoCategoryContentPlanner.DecideText(
            mapping.Target,
            mapping.EmptyValueHandling,
            current,
            source.Text);

        if (!decision.Change)
            return;

        plan.TextWrites.Add((mapping.Target, mapping.LanguageId, decision.Value));
        if (mapping.Target == AkeneoCategoryContentTarget.Name)
            plan.PlannedNames[mapping.LanguageId] = decision.Value;

        AddChange(plan, context, mapping, current, decision.Value);
    }

    private async Task PlanSlugAsync(
        CategoryPlan plan,
        RunContext context,
        AkeneoCategoryContentMapping mapping,
        AkeneoCategoryContentSource source)
    {
        var category = plan.Category;
        var fromName = source.Text == null;

        if (fromName && mapping.EmptyValueHandling != AkeneoCategoryContentEmptyValueHandling.Clear)
            return;

        // A cleared slug is rebuilt from the name, as nopCommerce does when the field is left blank.
        var name = plan.PlannedNames.TryGetValue(mapping.LanguageId, out var plannedName)
            ? plannedName
            : await GetCurrentTextAsync(category, AkeneoCategoryContentTarget.Name, mapping.LanguageId);
        if (string.IsNullOrWhiteSpace(name))
            name = category.Name;

        var requested = fromName ? name : source.Text;
        var wanted = await CleanSlugAsync(requested);

        if (string.IsNullOrWhiteSpace(wanted))
        {
            plan.Result.Errors.Add(
                $"The Akeneo URL slug '{requested}' has no characters nopCommerce allows in URLs.");
            return;
        }

        var proposed = await urlRecordService.ValidateSeNameAsync(
            category,
            fromName ? null : requested,
            name,
            true);

        // A slug left behind by a deleted category or product is moved here instead of failing.
        var reclaim = string.Equals(wanted, proposed, StringComparison.OrdinalIgnoreCase)
            ? null
            : await FindSlugHeldByDeletedEntitiesAsync(wanted, category.Id);

        if (reclaim != null)
        {
            proposed = wanted;
            plan.ReleasedSlugRecords.AddRange(reclaim.Records
                .Where(record => plan.ReleasedSlugRecords.All(released => released.Id != record.Id)));
            plan.Result.Warnings.Add(
                $"The URL slug '{wanted}' was held by deleted {string.Join(", ", reclaim.Holders)}, so it is released and given to this category.");
        }
        else if (!string.Equals(wanted, proposed, StringComparison.OrdinalIgnoreCase))
        {
            var origin = fromName ? "URL slug generated from the category name" : "Akeneo URL slug";
            plan.Result.Errors.Add(
                $"The {origin} '{wanted}' is {await DescribeSlugHolderAsync(wanted)}, so nopCommerce would save '{proposed}' instead. " +
                "The category was not updated; free the slug or change it in Akeneo, then sync again.");
            return;
        }

        if (!fromName && !string.Equals(wanted, requested.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            plan.Result.Warnings.Add(
                $"The Akeneo URL slug '{requested}' becomes '{proposed}' because it contains characters nopCommerce does not allow in URLs.");
        }

        var current = await urlRecordService.GetActiveSlugAsync(category.Id, nameof(Category), mapping.LanguageId) ?? string.Empty;
        if (string.Equals(current, proposed, StringComparison.OrdinalIgnoreCase))
            return;

        plan.SlugWrites.Add((mapping.LanguageId, proposed));
        AddChange(plan, context, mapping, current, proposed);
    }

    private async Task PlanPictureAsync(
        CategoryPlan plan,
        AkeneoCategoryContentMapping mapping,
        AkeneoCategoryContentSource source)
    {
        var category = plan.Category;
        var current = category.PictureId > 0 ? $"Picture ID {category.PictureId}" : string.Empty;

        if (source.Image == null)
        {
            if (mapping.EmptyValueHandling != AkeneoCategoryContentEmptyValueHandling.Clear || category.PictureId == 0)
                return;

            plan.Picture = new PictureWrite(null);
            plan.Result.Changes.Add(new AkeneoCategoryContentChange(
                AkeneoCategoryContentPlanner.DescribeTarget(mapping.Target), "Standard", Display(current), Display(string.Empty)));
            return;
        }

        var image = source.Image;
        var upToDate =
            OwnsCurrentPicture(plan) &&
            string.Equals(plan.State.PictureFilePath, image.ImageFilePath, StringComparison.Ordinal) &&
            await pictureService.GetPictureByIdAsync(category.PictureId) != null;

        if (upToDate)
            return;

        plan.Picture = new PictureWrite(image);
        plan.Result.Changes.Add(new AkeneoCategoryContentChange(
            AkeneoCategoryContentPlanner.DescribeTarget(mapping.Target),
            "Standard",
            Display(current),
            image.ImageFileName ?? image.ImageFilePath));
    }

    #endregion

    #region Applying

    private async Task ApplyAsync(CategoryPlan plan, CancellationToken cancellationToken)
    {
        var category = plan.Category;
        var image = plan.Picture?.Image;

        // Download before touching the category, so a failed download changes nothing.
        AkeneoBinaryFile file = null;
        string mimeType = null;
        if (image != null)
        {
            file = await apiClient.DownloadCategoryMediaFileAsync(image.ImageFilePath, cancellationToken);
            mimeType = file.ContentType ?? image.ImageMimeType;

            if (string.IsNullOrWhiteSpace(mimeType) || !mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Akeneo file '{image.ImageFileName ?? image.ImageFilePath}' is not an image.");
        }

        using var transaction = new TransactionScope(
            TransactionScopeOption.Required,
            new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted, Timeout = TimeSpan.FromMinutes(2) },
            TransactionScopeAsyncFlowOption.Enabled);

        var state = plan.State ?? new AkeneoCategoryContentState { AkeneoCategoryCode = plan.Result.AkeneoCode };
        var ownedPictureId = OwnsCurrentPicture(plan) ? category.PictureId : 0;
        var pictureToDelete = 0;

        foreach (var (target, _, value) in plan.TextWrites.Where(write => write.LanguageId == 0))
            SetProperty(category, target, value);

        if (plan.Picture != null)
        {
            if (image != null)
            {
                var seoFilename = await pictureService.GetPictureSeNameAsync(category.Name);
                var picture = ownedPictureId > 0 && await pictureService.GetPictureByIdAsync(ownedPictureId) != null
                    ? await pictureService.UpdatePictureAsync(ownedPictureId, file.Bytes, mimeType, seoFilename)
                    : await pictureService.InsertPictureAsync(file.Bytes, mimeType, seoFilename);

                category.PictureId = picture.Id;
                state.NopPictureId = picture.Id;
                state.PictureFilePath = image.ImageFilePath;
            }
            else
            {
                pictureToDelete = ownedPictureId;
                category.PictureId = 0;
                state.NopPictureId = null;
                state.PictureFilePath = null;
            }
        }

        if (plan.Picture != null || plan.TextWrites.Any(write => write.LanguageId == 0))
            await categoryService.UpdateCategoryAsync(category);

        foreach (var (target, languageId, value) in plan.TextWrites.Where(write => write.LanguageId > 0))
            await localizedEntityService.SaveLocalizedValueAsync(category, LocalizedSelector(target), value, languageId);

        if (plan.ReleasedSlugRecords.Count > 0)
            await urlRecordService.DeleteUrlRecordsAsync(plan.ReleasedSlugRecords);

        foreach (var (languageId, slug) in plan.SlugWrites)
            await urlRecordService.SaveSlugAsync(category, slug, languageId);

        if (pictureToDelete > 0 && await pictureService.GetPictureByIdAsync(pictureToDelete) is { } oldPicture)
            await pictureService.DeletePictureAsync(oldPicture);

        state.NopCategoryId = category.Id;
        state.LastStatus = AkeneoCategoryContentStatus.Updated;
        state.LastMessage = Message(plan.Result);
        state.LastSyncedOnUtc = DateTime.UtcNow;
        await contentMappingService.SaveStateAsync(state);
        plan.State = state;

        transaction.Complete();
    }

    private async Task SaveOutcomeAsync(CategoryPlan plan)
    {
        var state = plan.State ?? new AkeneoCategoryContentState { AkeneoCategoryCode = plan.Result.AkeneoCode };
        state.NopCategoryId = plan.Category.Id;
        state.LastStatus = plan.Result.Status;
        state.LastMessage = Message(plan.Result);
        state.LastSyncedOnUtc = DateTime.UtcNow;
        await contentMappingService.SaveStateAsync(state);
        plan.State = state;
    }

    private async Task LogItemAsync(AkeneoSyncRunRecord runRecord, AkeneoCategoryContentItemResult item)
    {
        var action = item.Status switch
        {
            AkeneoCategoryContentStatus.Updated => SyncItemActionType.Updated,
            AkeneoCategoryContentStatus.Failed => SyncItemActionType.Failed,
            AkeneoCategoryContentStatus.Skipped => SyncItemActionType.Skipped,
            _ => (SyncItemActionType?)null
        };

        // Unchanged categories are counted on the run but not logged one by one.
        if (action == null && item.Warnings.Count == 0)
            return;

        var subject = item.NopCategoryName != null
            ? $"Category '{item.NopCategoryName}' (ID {item.NopCategoryId})"
            : $"Category ID {item.NopCategoryId}";

        await syncItemLogService.InsertAkeneoSyncItemLogAsync(new AkeneoSyncItemLog
        {
            SyncRunRecordId = runRecord.Id,
            AkeneoIdentifier = item.AkeneoCode,
            ActionTypeId = (int)(action ?? SyncItemActionType.Skipped),
            Message = CommonHelper.EnsureMaximumLength($"{subject}: {Message(item)}", 4000),
            CreatedOnUtc = DateTime.UtcNow
        });
    }

    #endregion

    #region Helpers

    private static bool OwnsCurrentPicture(CategoryPlan plan) =>
        plan.Category.PictureId > 0 &&
        plan.State?.NopPictureId == plan.Category.PictureId;

    private async Task<string> GetCurrentTextAsync(Category category, AkeneoCategoryContentTarget target, int languageId)
    {
        if (languageId == 0)
            return GetProperty(category, target) ?? string.Empty;

        return await localizedEntityService.GetLocalizedValueAsync(
            languageId,
            category.Id,
            nameof(Category),
            PropertyName(target)) ?? string.Empty;
    }

    private static string GetProperty(Category category, AkeneoCategoryContentTarget target) => target switch
    {
        AkeneoCategoryContentTarget.Name => category.Name,
        AkeneoCategoryContentTarget.Description => category.Description,
        AkeneoCategoryContentTarget.MetaTitle => category.MetaTitle,
        AkeneoCategoryContentTarget.MetaDescription => category.MetaDescription,
        AkeneoCategoryContentTarget.MetaKeywords => category.MetaKeywords,
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, "Not a text property.")
    };

    private static void SetProperty(Category category, AkeneoCategoryContentTarget target, string value)
    {
        switch (target)
        {
            case AkeneoCategoryContentTarget.Name: category.Name = value; break;
            case AkeneoCategoryContentTarget.Description: category.Description = value; break;
            case AkeneoCategoryContentTarget.MetaTitle: category.MetaTitle = value; break;
            case AkeneoCategoryContentTarget.MetaDescription: category.MetaDescription = value; break;
            case AkeneoCategoryContentTarget.MetaKeywords: category.MetaKeywords = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(target), target, "Not a text property.");
        }
    }

    private static string PropertyName(AkeneoCategoryContentTarget target) => target switch
    {
        AkeneoCategoryContentTarget.Name => nameof(Category.Name),
        AkeneoCategoryContentTarget.Description => nameof(Category.Description),
        AkeneoCategoryContentTarget.MetaTitle => nameof(Category.MetaTitle),
        AkeneoCategoryContentTarget.MetaDescription => nameof(Category.MetaDescription),
        AkeneoCategoryContentTarget.MetaKeywords => nameof(Category.MetaKeywords),
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, "Not a text property.")
    };

    private static Expression<Func<Category, string>> LocalizedSelector(AkeneoCategoryContentTarget target) => target switch
    {
        AkeneoCategoryContentTarget.Name => category => category.Name,
        AkeneoCategoryContentTarget.Description => category => category.Description,
        AkeneoCategoryContentTarget.MetaTitle => category => category.MetaTitle,
        AkeneoCategoryContentTarget.MetaDescription => category => category.MetaDescription,
        AkeneoCategoryContentTarget.MetaKeywords => category => category.MetaKeywords,
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, "Not a text property.")
    };

    /// <summary>The slug as nopCommerce would clean it, before making it unique.</summary>
    private async Task<string> CleanSlugAsync(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var slug = await urlRecordService.GetSeNameAsync(
            value.Trim(),
            seoSettings.ConvertNonWesternChars,
            seoSettings.AllowUnicodeCharsInUrls);

        return CommonHelper.EnsureMaximumLength(slug, NopSeoDefaults.SearchEngineNameLength);
    }

    private sealed record DeletedSlugHolders(IReadOnlyList<UrlRecord> Records, IReadOnlyList<string> Holders);

    /// <summary>
    /// Returns the URL records holding the slug when every one belongs to a
    /// category or product that is deleted (or no longer exists), so the slug
    /// can be moved to this category instead of being numbered. Returns null
    /// when the slug is reserved or any holder is still live.
    /// </summary>
    private async Task<DeletedSlugHolders> FindSlugHeldByDeletedEntitiesAsync(string slug, int categoryId)
    {
        if (seoSettings.ReservedUrlRecordSlugs?.Contains(slug, StringComparer.InvariantCultureIgnoreCase) == true ||
            (await languageService.GetAllLanguagesAsync(true)).Any(language =>
                string.Equals(language.UniqueSeoCode, slug, StringComparison.InvariantCultureIgnoreCase)))
        {
            return null;
        }

        var records = (await urlRecordRepository.Table
                .Where(record => record.Slug == slug)
                .ToListAsync())
            .Where(record => record.Slug.Equals(slug, StringComparison.InvariantCultureIgnoreCase) &&
                             !(record.EntityId == categoryId &&
                               record.EntityName.Equals(nameof(Category), StringComparison.InvariantCultureIgnoreCase)))
            .ToList();

        if (records.Count == 0)
            return null;

        var holders = new List<string>();
        foreach (var (entityName, entityId) in records.Select(record => (record.EntityName, record.EntityId)).Distinct())
        {
            if (entityName.Equals(nameof(Category), StringComparison.InvariantCultureIgnoreCase))
            {
                var holder = await categoryService.GetCategoryByIdAsync(entityId);
                if (holder is { Deleted: false })
                    return null;

                holders.Add(holder == null ? $"category ID {entityId}" : $"category '{holder.Name}' (ID {entityId})");
            }
            else if (entityName.Equals(nameof(Product), StringComparison.InvariantCultureIgnoreCase))
            {
                var holder = await productService.GetProductByIdAsync(entityId);
                if (holder is { Deleted: false })
                    return null;

                holders.Add(holder == null ? $"product ID {entityId}" : $"product '{holder.Name}' (ID {entityId})");
            }
            else
            {
                return null;
            }
        }

        return new DeletedSlugHolders(records, holders);
    }

    private async Task<string> DescribeSlugHolderAsync(string slug)
    {
        var record = await urlRecordService.GetBySlugAsync(slug);

        if (record == null)
            return "reserved by nopCommerce";

        if (!record.EntityName.Equals(nameof(Category), StringComparison.InvariantCultureIgnoreCase))
            return $"already used by {record.EntityName} ID {record.EntityId}";

        var holder = await categoryService.GetCategoryByIdAsync(record.EntityId);
        return holder == null
            ? $"already used by category ID {record.EntityId}"
            : $"already used by category '{holder.Name}' (ID {holder.Id})";
    }

    private static void AddChange(
        CategoryPlan plan,
        RunContext context,
        AkeneoCategoryContentMapping mapping,
        string from,
        string to)
    {
        plan.Result.Changes.Add(new AkeneoCategoryContentChange(
            AkeneoCategoryContentPlanner.DescribeTarget(mapping.Target),
            mapping.LanguageId == 0 ? "Standard" : context.LanguageNames[mapping.LanguageId],
            Display(from),
            Display(to)));
    }

    private static string Display(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "(empty)";

        var text = Whitespace.Replace(value, " ").Trim();
        return text.Length <= DisplayLength ? text : text[..(DisplayLength - 1)] + "…";
    }

    private static string Message(AkeneoCategoryContentItemResult item)
    {
        var parts = new List<string>();
        if (item.Errors.Count > 0)
            parts.Add(string.Join(" ", item.Errors));
        if (item.Status == AkeneoCategoryContentStatus.Updated && item.Changes.Count > 0)
            parts.Add("Updated " + string.Join(", ", item.Changes
                .Select(change => change.Language == "Standard" ? change.Property : $"{change.Property} ({change.Language})")
                .Distinct()) + ".");
        if (item.Warnings.Count > 0)
            parts.Add(string.Join(" ", item.Warnings));

        return parts.Count == 0 ? null : string.Join(" ", parts);
    }

    private static string Summarize(AkeneoCategoryContentRunResult result)
    {
        var verb = result.Applied ? "updated" : "would change";
        return $"{result.Categories.Count} mapped categor{(result.Categories.Count == 1 ? "y" : "ies")}: " +
               $"{result.UpdatedCount} {verb}, {result.UnchangedCount} unchanged, " +
               $"{result.SkippedCount} skipped, {result.FailedCount} failed.";
    }

    private static void Finish(AkeneoSyncRunRecord runRecord, AkeneoCategoryContentRunResult result)
    {
        runRecord.FinishedOnUtc = DateTime.UtcNow;
        runRecord.TotalRead = result.Categories.Count;
        runRecord.UpdatedCount = result.UpdatedCount;
        runRecord.SkippedCount = result.SkippedCount + result.UnchangedCount;
        runRecord.FailedCount = result.FailedCount;
        runRecord.WarningCount = result.WarningCount;
    }

    #endregion
}
