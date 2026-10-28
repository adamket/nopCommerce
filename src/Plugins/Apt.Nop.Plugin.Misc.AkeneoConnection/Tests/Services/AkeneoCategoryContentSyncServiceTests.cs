using System.Linq.Expressions;
using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Categories;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Localization;
using Nop.Core.Domain.Media;
using Nop.Core.Domain.Seo;
using Nop.Data;
using Nop.Services.Catalog;
using Nop.Services.Localization;
using Nop.Services.Media;
using Nop.Services.Seo;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

/// <summary>
/// Akeneo owns mapped category content: previews write nothing, syncs overwrite
/// nopCommerce edits, and a category with any problem is left untouched.
/// </summary>
[TestFixture]
public class AkeneoCategoryContentSyncServiceTests
{
    private const string ImagePath = "8/8/3/d/883d_shade.jpg";

    private Mock<IAkeneoApiClient> _api = null!;
    private Mock<IAkeneoCategoryContentMappingService> _content = null!;
    private Mock<IAkeneoNopEntityMappingService> _entities = null!;
    private Mock<ICategoryService> _categories = null!;
    private Mock<ILanguageService> _languages = null!;
    private Mock<ILocalizedEntityService> _localized = null!;
    private Mock<IUrlRecordService> _urls = null!;
    private Mock<IPictureService> _pictures = null!;
    private Mock<IAkeneoSyncLeaseService> _leases = null!;
    private Mock<IAkeneoSyncRunRecordService> _runs = null!;
    private Mock<IAkeneoSyncItemLogService> _itemLogs = null!;
    private Mock<IProductService> _products = null!;
    private List<UrlRecord> _urlRecords = null!;

    private List<AkeneoCategoryContentMapping> _mappings = null!;
    private AkeneoCategoryContentState? _state;
    private Category _category = null!;

    [SetUp]
    public void SetUp()
    {
        _api = new Mock<IAkeneoApiClient>();
        _content = new Mock<IAkeneoCategoryContentMappingService>();
        _entities = new Mock<IAkeneoNopEntityMappingService>();
        _categories = new Mock<ICategoryService>();
        _languages = new Mock<ILanguageService>();
        _localized = new Mock<ILocalizedEntityService>();
        _urls = new Mock<IUrlRecordService>();
        _pictures = new Mock<IPictureService>();
        _leases = new Mock<IAkeneoSyncLeaseService>();
        _runs = new Mock<IAkeneoSyncRunRecordService>();
        _itemLogs = new Mock<IAkeneoSyncItemLogService>();
        _products = new Mock<IProductService>();
        _urlRecords = new List<UrlRecord>();

        _mappings = new List<AkeneoCategoryContentMapping>();
        _state = null;
        _category = new Category { Id = 7, Name = "Shade Trees", Description = "Edited in nopCommerce" };

        _content.Setup(s => s.GetAllMappingsAsync()).ReturnsAsync(() => _mappings);
        _content.Setup(s => s.GetStateAsync("shade_trees")).ReturnsAsync(() => _state);
        _entities.Setup(s => s.GetAkeneoNopEntityMappingsAsync(AkeneoEntityType.Category)).ReturnsAsync(new List<AkeneoNopEntityMapping>
        {
            new() { AkeneoEntityTypeId = (int)AkeneoEntityType.Category, AkeneoCode = "shade_trees", NopEntityTypeId = (int)NopEntityType.Category, NopEntityId = 7 }
        });
        _categories.Setup(s => s.GetCategoryByIdAsync(7)).ReturnsAsync(() => _category);
        _languages.Setup(s => s.GetAllLanguagesAsync(true, 0)).ReturnsAsync(new List<Language>
        {
            new() { Id = 1, Name = "English", LanguageCulture = "en-US" },
            new() { Id = 2, Name = "Español", LanguageCulture = "es-ES" }
        });
        _api.Setup(s => s.GetCategoriesWithValuesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AkeneoCategoryDefinition> { ShadeTrees() });
        _pictures.Setup(s => s.GetPictureSeNameAsync(It.IsAny<string>())).ReturnsAsync("shade-trees");
        _leases.Setup(s => s.TryAcquireAsync(AkeneoConnectionConstants.CatalogWriterLockKey, It.IsAny<TimeSpan>()))
            .ReturnsAsync(new AkeneoSyncLease { Id = 1 });
    }

    private AkeneoCategoryContentSyncService Service() => new(
        _api.Object, _content.Object, _entities.Object, _categories.Object, _languages.Object,
        _localized.Object, _urls.Object, _pictures.Object, _leases.Object, _runs.Object, _itemLogs.Object,
        _products.Object, UrlRecordRepository(),
        new SeoSettings());

    private IRepository<UrlRecord> UrlRecordRepository()
    {
        var repository = new Mock<IRepository<UrlRecord>>();
        repository.Setup(r => r.Table).Returns(() => _urlRecords.AsQueryable());
        return repository.Object;
    }

    /// <summary>Maps the Akeneo slug, which nopCommerce numbers because the given entity holds it.</summary>
    private UrlRecord SlugHeldBy(string entityName, int entityId)
    {
        if (!_mappings.Any(mapping => mapping.Target == AkeneoCategoryContentTarget.SeName))
            Map(AkeneoCategoryContentTarget.SeName, "url_slug");
        _urls.Setup(s => s.GetSeNameAsync("shade-trees", It.IsAny<bool>(), It.IsAny<bool>())).ReturnsAsync("shade-trees");
        _urls.Setup(s => s.ValidateSeNameAsync(_category, "shade-trees", It.IsAny<string>(), true)).ReturnsAsync("shade-trees-2");

        var record = new UrlRecord { Id = _urlRecords.Count + 1, EntityName = entityName, EntityId = entityId, Slug = "shade-trees", IsActive = true };
        _urlRecords.Add(record);
        _urls.Setup(s => s.GetBySlugAsync("shade-trees")).ReturnsAsync(_urlRecords[0]);
        return record;
    }

    private static AkeneoCategoryDefinition ShadeTrees() => JsonSerializer.Deserialize<AkeneoCategoryDefinition>($$"""
        {
          "code": "shade_trees",
          "labels": { "en_US": "Shade Trees", "es_ES": "Árboles de sombra" },
          "values": {
            "description|a|ecommerce|en_US": { "data": "<p>From Akeneo</p>", "type": "richtext", "channel": "ecommerce", "locale": "en_US", "attribute_code": "description|a" },
            "url_slug|b|ecommerce|en_US": { "data": "shade-trees", "type": "text", "channel": "ecommerce", "locale": "en_US", "attribute_code": "url_slug|b" },
            "image|c": { "data": { "file_path": "{{ImagePath}}", "original_filename": "shade.jpg", "mime_type": "image/jpeg" }, "type": "image", "channel": null, "locale": null, "attribute_code": "image|c" }
          }
        }
        """)!;

    private void Map(AkeneoCategoryContentTarget target, string? attribute, string? locale = "en_US", string? channel = "ecommerce", int languageId = 0) =>
        _mappings.Add(new AkeneoCategoryContentMapping
        {
            Id = _mappings.Count + 1,
            Target = target,
            LanguageId = languageId,
            SourceType = attribute == null ? AkeneoCategoryContentSourceType.Label : AkeneoCategoryContentSourceType.TemplateAttribute,
            AkeneoAttributeCode = attribute,
            Locale = locale,
            Channel = attribute == null ? null : channel
        });

    [Test]
    public async Task A_preview_reports_the_overwrite_without_writing()
    {
        Map(AkeneoCategoryContentTarget.Description, "description");

        var result = await Service().PreviewAsync();

        var item = result.Categories.Single();
        Assert.Multiple(() =>
        {
            Assert.That(item.Status, Is.EqualTo(AkeneoCategoryContentStatus.Updated));
            Assert.That(item.Changes.Single(), Is.EqualTo(new AkeneoCategoryContentChange(
                "Description", "Standard", "Edited in nopCommerce", "<p>From Akeneo</p>")));
            Assert.That(result.Applied, Is.False);
        });
        _categories.Verify(s => s.UpdateCategoryAsync(It.IsAny<Category>()), Times.Never);
        _leases.Verify(s => s.TryAcquireAsync(It.IsAny<string>(), It.IsAny<TimeSpan>()), Times.Never);
    }

    [Test]
    public async Task A_sync_overwrites_the_nopcommerce_edit_and_records_the_run()
    {
        Map(AkeneoCategoryContentTarget.Description, "description");

        var result = await Service().SyncAsync();

        Assert.That(result.UpdatedCount, Is.EqualTo(1));
        _categories.Verify(s => s.UpdateCategoryAsync(It.Is<Category>(c => c.Description == "<p>From Akeneo</p>")), Times.Once);
        _content.Verify(s => s.SaveStateAsync(It.Is<AkeneoCategoryContentState>(state =>
            state.AkeneoCategoryCode == "shade_trees" && state.LastStatus == AkeneoCategoryContentStatus.Updated)), Times.Once);
        _runs.Verify(s => s.UpdateAkeneoSyncRunRecordAsync(It.Is<AkeneoSyncRunRecord>(run =>
            run.SyncTypeId == (int)SyncType.CategoryContent && run.SyncStatusId == (int)SyncStatus.Completed && run.UpdatedCount == 1)), Times.Once);
        _leases.Verify(s => s.ReleaseAsync(It.IsAny<AkeneoSyncLease>()), Times.Once);
    }

    [Test]
    public async Task A_taken_slug_fails_the_category_and_writes_nothing()
    {
        Map(AkeneoCategoryContentTarget.Description, "description");
        Map(AkeneoCategoryContentTarget.SeName, "url_slug");
        _urls.Setup(s => s.GetSeNameAsync("shade-trees", It.IsAny<bool>(), It.IsAny<bool>())).ReturnsAsync("shade-trees");
        _urls.Setup(s => s.ValidateSeNameAsync(_category, "shade-trees", It.IsAny<string>(), true)).ReturnsAsync("shade-trees-2");
        _urls.Setup(s => s.GetBySlugAsync("shade-trees")).ReturnsAsync(new UrlRecord { EntityName = nameof(Category), EntityId = 9, Slug = "shade-trees" });
        _categories.Setup(s => s.GetCategoryByIdAsync(9)).ReturnsAsync(new Category { Id = 9, Name = "Other Trees" });

        var result = await Service().SyncAsync();

        var item = result.Categories.Single();
        Assert.Multiple(() =>
        {
            Assert.That(item.Status, Is.EqualTo(AkeneoCategoryContentStatus.Failed));
            Assert.That(item.Errors.Single(), Does.Contain("already used by category 'Other Trees' (ID 9)").And.Contain("'shade-trees-2'"));
            Assert.That(result.Success, Is.False);
        });
        _categories.Verify(s => s.UpdateCategoryAsync(It.IsAny<Category>()), Times.Never);
        _urls.Verify(s => s.SaveSlugAsync(It.IsAny<Category>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        _content.Verify(s => s.SaveStateAsync(It.Is<AkeneoCategoryContentState>(state => state.LastStatus == AkeneoCategoryContentStatus.Failed)), Times.Once);
    }

    [Test]
    public async Task A_slug_held_by_a_deleted_category_is_moved_to_the_synced_category()
    {
        var held = SlugHeldBy(nameof(Category), 9);
        _categories.Setup(s => s.GetCategoryByIdAsync(9)).ReturnsAsync(new Category { Id = 9, Name = "Old Shade Trees", Deleted = true });

        var result = await Service().SyncAsync();

        var item = result.Categories.Single();
        Assert.Multiple(() =>
        {
            Assert.That(item.Status, Is.EqualTo(AkeneoCategoryContentStatus.Updated));
            Assert.That(item.Warnings.Single(), Does.Contain("deleted category 'Old Shade Trees' (ID 9)"));
        });
        _urls.Verify(s => s.DeleteUrlRecordsAsync(It.Is<IList<UrlRecord>>(records => records.Single() == held)), Times.Once);
        _urls.Verify(s => s.SaveSlugAsync(_category, "shade-trees", 0), Times.Once);
    }

    [Test]
    public async Task A_slug_held_by_a_missing_or_deleted_product_is_moved_to_the_synced_category()
    {
        SlugHeldBy(nameof(Category), 9);
        SlugHeldBy(nameof(Product), 30);
        _products.Setup(s => s.GetProductByIdAsync(30)).ReturnsAsync(new Product { Id = 30, Name = "Red Maple", Deleted = true });

        var result = await Service().SyncAsync();

        Assert.That(result.Categories.Single().Status, Is.EqualTo(AkeneoCategoryContentStatus.Updated));
        _urls.Verify(s => s.DeleteUrlRecordsAsync(It.Is<IList<UrlRecord>>(records => records.Count == 2)), Times.Once);
        _urls.Verify(s => s.SaveSlugAsync(_category, "shade-trees", 0), Times.Once);
    }

    [Test]
    public async Task A_slug_also_held_by_a_live_entity_still_fails()
    {
        SlugHeldBy(nameof(Category), 9);
        SlugHeldBy(nameof(Product), 30);
        _products.Setup(s => s.GetProductByIdAsync(30)).ReturnsAsync(new Product { Id = 30, Name = "Red Maple" });

        var result = await Service().SyncAsync();

        Assert.That(result.Categories.Single().Status, Is.EqualTo(AkeneoCategoryContentStatus.Failed));
        _urls.Verify(s => s.DeleteUrlRecordsAsync(It.IsAny<IList<UrlRecord>>()), Times.Never);
        _urls.Verify(s => s.SaveSlugAsync(It.IsAny<Category>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task A_preview_reports_the_reclaim_without_releasing_the_slug()
    {
        SlugHeldBy(nameof(Category), 9);

        var result = await Service().PreviewAsync();

        Assert.That(result.Categories.Single().Status, Is.EqualTo(AkeneoCategoryContentStatus.Updated));
        _urls.Verify(s => s.DeleteUrlRecordsAsync(It.IsAny<IList<UrlRecord>>()), Times.Never);
    }

    [Test]
    public async Task An_unchanged_owned_picture_is_not_downloaded_again()
    {
        Map(AkeneoCategoryContentTarget.Picture, "image", locale: null, channel: null);
        _category.PictureId = 50;
        _state = new AkeneoCategoryContentState { Id = 3, AkeneoCategoryCode = "shade_trees", NopPictureId = 50, PictureFilePath = ImagePath };
        _pictures.Setup(s => s.GetPictureByIdAsync(50)).ReturnsAsync(new Picture { Id = 50 });

        var result = await Service().SyncAsync();

        Assert.That(result.Categories.Single().Status, Is.EqualTo(AkeneoCategoryContentStatus.Unchanged));
        _api.Verify(s => s.DownloadCategoryMediaFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task A_new_image_replaces_the_owned_picture_in_place()
    {
        Map(AkeneoCategoryContentTarget.Picture, "image", locale: null, channel: null);
        _category.PictureId = 50;
        _state = new AkeneoCategoryContentState { Id = 3, AkeneoCategoryCode = "shade_trees", NopPictureId = 50, PictureFilePath = "old/path.jpg" };
        _pictures.Setup(s => s.GetPictureByIdAsync(50)).ReturnsAsync(new Picture { Id = 50 });
        _pictures.Setup(s => s.UpdatePictureAsync(50, It.IsAny<byte[]>(), "image/jpeg", "shade-trees", null, null, true, true))
            .ReturnsAsync(new Picture { Id = 50 });
        _api.Setup(s => s.DownloadCategoryMediaFileAsync(ImagePath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AkeneoBinaryFile { Bytes = [1, 2, 3], ContentType = "image/jpeg" });

        await Service().SyncAsync();

        _pictures.Verify(s => s.UpdatePictureAsync(50, It.IsAny<byte[]>(), "image/jpeg", "shade-trees", null, null, true, true), Times.Once);
        _pictures.Verify(s => s.InsertPictureAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never);
        _content.Verify(s => s.SaveStateAsync(It.Is<AkeneoCategoryContentState>(state => state.PictureFilePath == ImagePath && state.NopPictureId == 50)), Times.Once);
    }

    [Test]
    public async Task A_picture_set_by_hand_is_replaced_with_a_new_one_and_not_deleted()
    {
        Map(AkeneoCategoryContentTarget.Picture, "image", locale: null, channel: null);
        _category.PictureId = 12;
        _pictures.Setup(s => s.InsertPictureAsync(It.IsAny<byte[]>(), "image/jpeg", "shade-trees", null, null, true, true))
            .ReturnsAsync(new Picture { Id = 60 });
        _api.Setup(s => s.DownloadCategoryMediaFileAsync(ImagePath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AkeneoBinaryFile { Bytes = [1, 2, 3], ContentType = "image/jpeg" });

        await Service().SyncAsync();

        _categories.Verify(s => s.UpdateCategoryAsync(It.Is<Category>(c => c.PictureId == 60)), Times.Once);
        _pictures.Verify(s => s.DeletePictureAsync(It.IsAny<Picture>()), Times.Never);
    }

    [Test]
    public async Task A_language_specific_mapping_writes_the_localized_value_only()
    {
        Map(AkeneoCategoryContentTarget.Name, null, locale: "es_ES", languageId: 2);
        _localized.Setup(s => s.GetLocalizedValueAsync(2, 7, nameof(Category), nameof(Category.Name))).ReturnsAsync(string.Empty);

        var result = await Service().SyncAsync();

        Assert.That(result.Categories.Single().Changes.Single().Language, Is.EqualTo("Español"));
        _localized.Verify(s => s.SaveLocalizedValueAsync(_category, It.IsAny<Expression<Func<Category, string>>>(), "Árboles de sombra", 2), Times.Once);
        _categories.Verify(s => s.UpdateCategoryAsync(It.IsAny<Category>()), Times.Never);
    }

    private void TranslateInto(params string[] locales) =>
        _content.Setup(s => s.GetTranslationLocalesAsync()).ReturnsAsync(locales.ToList());

    [Test]
    public async Task A_standard_mapping_is_translated_into_the_matching_language()
    {
        Map(AkeneoCategoryContentTarget.Name, null);
        TranslateInto("es_ES", "fr_FR");

        var result = await Service().SyncAsync();

        var change = result.Categories.Single().Changes.Single();
        Assert.That((change.Language, change.To), Is.EqualTo(("Español", "Árboles de sombra")));
        _localized.Verify(s => s.SaveLocalizedValueAsync(_category, It.IsAny<Expression<Func<Category, string>>>(), "Árboles de sombra", 2), Times.Once);
    }

    [Test]
    public async Task A_translation_akeneo_does_not_have_removes_the_localized_value()
    {
        Map(AkeneoCategoryContentTarget.Description, "description");
        TranslateInto("es_ES");
        _category.Description = "<p>From Akeneo</p>";
        _localized.Setup(s => s.GetLocalizedValueAsync(2, 7, nameof(Category), nameof(Category.Description))).ReturnsAsync("Old Spanish text");

        var result = await Service().SyncAsync();

        Assert.That(result.Categories.Single().Changes.Single().To, Is.EqualTo("(empty)"));
        _localized.Verify(s => s.SaveLocalizedValueAsync(_category, It.IsAny<Expression<Func<Category, string>>>(), string.Empty, 2), Times.Once);
    }

    [Test]
    public async Task A_language_specific_mapping_overrides_the_automatic_translation()
    {
        Map(AkeneoCategoryContentTarget.Name, null);
        Map(AkeneoCategoryContentTarget.Name, "description", languageId: 2);
        TranslateInto("es_ES");

        await Service().SyncAsync();

        _localized.Verify(s => s.SaveLocalizedValueAsync(_category, It.IsAny<Expression<Func<Category, string>>>(), "<p>From Akeneo</p>", 2), Times.Once);
        _localized.Verify(s => s.SaveLocalizedValueAsync(_category, It.IsAny<Expression<Func<Category, string>>>(), "Árboles de sombra", 2), Times.Never);
    }

    [Test]
    public async Task A_running_catalog_writer_blocks_the_sync()
    {
        Map(AkeneoCategoryContentTarget.Description, "description");
        _leases.Setup(s => s.TryAcquireAsync(It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync((AkeneoSyncLease)null!);

        var result = await Service().SyncAsync();

        Assert.That((result.Success, result.AlreadyRunning), Is.EqualTo((false, true)));
        _api.Verify(s => s.GetCategoriesWithValuesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Without_mappings_nothing_is_read_or_recorded()
    {
        var result = await Service().SyncAsync();

        Assert.That(result.Message, Does.Contain("No category content mappings"));
        _api.Verify(s => s.GetCategoriesWithValuesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        _runs.Verify(s => s.InsertAkeneoSyncRunRecordAsync(It.IsAny<AkeneoSyncRunRecord>()), Times.Never);
    }
}
