using System.Linq.Expressions;
using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Factories;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.TestData;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Moq;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Localization;
using Nop.Core.Events;
using Nop.Data;
using Nop.Services.Catalog;
using Nop.Services.Localization;
using Nop.Services.Seo;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

/// <summary>
/// The primary locale fills the base product fields; each further profile
/// locale writes nopCommerce translations for the language with the matching
/// culture, and a translation Akeneo no longer has is removed.
/// </summary>
[TestFixture]
public class AkeneoProductLocalizationTests
{
    #region Profile locales

    [Test]
    public void The_first_profile_locale_is_primary_and_the_rest_are_secondary()
    {
        var request = new AkeneoProductBatchImportRequestFactory(new AkeneoProductSearchJsonBuilder())
            .CreateFromProfile(new AkeneoSyncProfile { Id = 1, AkeneoLocales = "en_US, es_US,fr_FR,en_US,es_us", AkeneoChannel = "ecommerce" }, 1);

        Assert.Multiple(() =>
        {
            Assert.That(request.Locale, Is.EqualTo("en_US"));
            Assert.That(request.SecondaryLocales, Is.EqualTo(new[] { "es_US", "fr_FR" }));
        });
    }

    [TestCase("es_US", "es-US", true)]
    [TestCase("es_US", "es-ES", false)]
    [TestCase("fr_FR", "fr-fr", true)]
    public void Locales_match_nopcommerce_cultures(string locale, string culture, bool expected) =>
        Assert.That(AkeneoLocalizableTargets.MatchesCulture(locale, culture), Is.EqualTo(expected));

    #endregion

    #region Resolution

    [Test]
    public async Task Only_real_translations_of_localizable_fields_become_localized_values()
    {
        var intent = await Service().ResolveIntentAsync(
            Source(),
            AkeneoEntityType.Product,
            new AkeneoProductImportRequest
            {
                SyncProfileId = 7, SyncRunRecordId = 1, Locale = "en_US", Channel = "ecommerce",
                SecondaryLocales = ["es_US", "fr_FR"]
            },
            "trees",
            null,
            AkeneoAttributeMappingEntityScope.StandaloneProduct);

        var byKey = intent.LocalizedValues.ToDictionary(value => value.TargetKey, value => value);

        Assert.Multiple(() =>
        {
            Assert.That(byKey["Name"].Value, Is.EqualTo("Arce rojo"));
            Assert.That(byKey["Name"].LanguageId, Is.EqualTo(2));
            Assert.That(byKey["FullDescription"].Value, Is.Null, "no es_US description, so the Spanish one is removed");
            Assert.That(byKey["ShortDescription"].Value, Is.Null, "a non-localizable attribute gives the English text");
            Assert.That(byKey.ContainsKey("MetaTitle"), Is.False, "a mapping fixed to en_US is base-only");
            Assert.That(byKey.ContainsKey("Price"), Is.False, "price is not localizable");
            Assert.That(intent.LocalizedValues.All(value => value.Locale == "es_US"), Is.True, "fr_FR has no nopCommerce language");
        });
    }

    private static AkeneoProductDefinition Source() => new()
    {
        Identifier = "SKU", Uuid = "uuid", Family = "trees", Enabled = true,
        Values = JsonSerializer.SerializeToElement(new Dictionary<string, object>
        {
            ["name"] = new[]
            {
                new { data = "Red Maple", locale = (string?)"en_US", scope = (string?)null },
                new { data = "Arce rojo", locale = (string?)"es_US", scope = (string?)null }
            },
            ["description"] = new[] { new { data = "<p>A shade tree.</p>", locale = (string?)"en_US", scope = (string?)null } },
            ["summary"] = new[] { new { data = "Fast growing", locale = (string?)null, scope = (string?)null } },
            ["price"] = new[] { new { data = "24.99", locale = (string?)null, scope = (string?)null } }
        })
    };

    private static AkeneoProductSyncService Service()
    {
        AkeneoAttributeMapping Map(string code, NopTargetType type, string key, string? locale = null) => new()
        {
            Name = code, AkeneoAttributeCode = code, NopTargetTypeId = (int)type, NopTargetKey = key, Locale = locale
        };

        var mappings = new Mock<IAkeneoAttributeMappingService>();
        mappings.Setup(x => x.GetEffectiveMappingsAsync(It.IsAny<string>())).ReturnsAsync(new List<AkeneoAttributeMapping>
        {
            Map("name", NopTargetType.ProductField, "Name"),
            Map("description", NopTargetType.ProductField, "FullDescription"),
            Map("summary", NopTargetType.ProductField, "ShortDescription"),
            Map("name", NopTargetType.SeoField, "MetaTitle", locale: "en_US"),
            Map("price", NopTargetType.ProductField, "Price")
        });
        mappings.Setup(x => x.GetAllFallbackSourcesAsync()).ReturnsAsync(new List<AkeneoAttributeMappingFallbackSource>());
        var assets = new Mock<IAkeneoAssetMappingService>();
        assets.Setup(x => x.GetEffectiveMappingsAsync(It.IsAny<string>())).ReturnsAsync(new List<AkeneoAssetMapping>());
        var languages = new Mock<ILanguageService>();
        languages.Setup(x => x.GetAllLanguagesAsync(true, 0)).ReturnsAsync(new List<Language>
        {
            new() { Id = 1, Name = "English", LanguageCulture = "en-US" },
            new() { Id = 2, Name = "Español", LanguageCulture = "es-US" }
        });
        var products = new Mock<IProductService>();

        return new AkeneoProductSyncService(new AkeneoProductValueResolver(), Mock.Of<IAkeneoReferenceEntityValueResolver>(),
            Mock.Of<IAkeneoValueTransformationService>(), new AkeneoValueTemplateRenderer(new AkeneoProductValueResolver()),
            mappings.Object, assets.Object, Mock.Of<IAkeneoNopEntityMappingService>(), Mock.Of<IAkeneoProductSyncStateService>(),
            products.Object,
            new AkeneoProductSyncPipeline(Array.Empty<IAkeneoProductSectionSynchronizer>(), Mock.Of<IEventPublisher>()),
            new AkeneoProfileRuleEvaluator(Mock.Of<IAkeneoApiClient>()),
            AkeneoAttributeCatalogTestData.Create("name", "description", "summary", "price"),
            languages.Object);
    }

    #endregion

    #region Writing

    private Mock<ILocalizedEntityService> _localized = null!;
    private Mock<IUrlRecordService> _urls = null!;

    [SetUp]
    public void SetUp()
    {
        _localized = new Mock<ILocalizedEntityService>();
        _urls = new Mock<IUrlRecordService>();
    }

    [Test]
    public async Task A_translation_is_written_as_the_languages_localized_value()
    {
        var context = Context(new AkeneoLocalizedValue(NopTargetType.ProductField, "Name", "es_US", 2, "Español", "Arce rojo", "name"));

        await Synchronizer().SynchronizeAsync(context);

        _localized.Verify(s => s.SaveLocalizedValueAsync(context.Product, It.IsAny<Expression<Func<Product, string>>>(), "Arce rojo", 2), Times.Once);
        Assert.That(context.HasChanges, Is.True);
    }

    [Test]
    public async Task A_translation_removed_in_Akeneo_is_removed_from_nopCommerce()
    {
        _localized.Setup(s => s.GetLocalizedValueAsync(2, 100, nameof(Product), "FullDescription")).ReturnsAsync("<p>Un árbol.</p>");
        var context = Context(new AkeneoLocalizedValue(NopTargetType.ProductField, "FullDescription", "es_US", 2, "Español", null, "description"));

        await Synchronizer().SynchronizeAsync(context);

        _localized.Verify(s => s.SaveLocalizedValueAsync(context.Product, It.IsAny<Expression<Func<Product, string>>>(), string.Empty, 2), Times.Once);
    }

    [Test]
    public async Task No_translation_and_nothing_stored_writes_nothing()
    {
        var context = Context(new AkeneoLocalizedValue(NopTargetType.ProductField, "FullDescription", "es_US", 2, "Español", null, "description"));

        await Synchronizer().SynchronizeAsync(context);

        _localized.Verify(s => s.SaveLocalizedValueAsync(It.IsAny<Product>(), It.IsAny<Expression<Func<Product, string>>>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task A_taken_Spanish_slug_is_skipped_with_a_warning_instead_of_failing_the_product()
    {
        _urls.Setup(s => s.GetSeNameAsync("arce-rojo", It.IsAny<bool>(), It.IsAny<bool>())).ReturnsAsync("arce-rojo");
        _urls.Setup(s => s.ValidateSeNameAsync(100, nameof(Product), "arce-rojo", null, false)).ReturnsAsync("arce-rojo-2");
        var context = Context(new AkeneoLocalizedValue(NopTargetType.SeoField, "SeName", "es_US", 2, "Español", "arce-rojo", "url_slug"));

        await Synchronizer().SynchronizeAsync(context);

        _urls.Verify(s => s.SaveSlugAsync(It.IsAny<Product>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        Assert.Multiple(() =>
        {
            Assert.That(context.Result.Success, Is.True);
            Assert.That(context.Result.Warnings.Single(), Does.Contain("Español URL slug 'arce-rojo'"));
        });
    }

    [Test]
    public async Task A_removed_Spanish_slug_is_deactivated()
    {
        _urls.Setup(s => s.GetActiveSlugAsync(100, nameof(Product), 2)).ReturnsAsync("arce-rojo");
        var context = Context(new AkeneoLocalizedValue(NopTargetType.SeoField, "SeName", "es_US", 2, "Español", null, "url_slug"));

        await Synchronizer().SynchronizeAsync(context);

        _urls.Verify(s => s.SaveSlugAsync(context.Product, string.Empty, 2), Times.Once);
    }

    [Test]
    public async Task Dry_run_lists_each_translation_by_language()
    {
        var model = new AkeneoProductMappingPreviewModel();

        await Synchronizer().PlanAsync(
            Context(new AkeneoLocalizedValue(NopTargetType.ProductField, "Name", "es_US", 2, "Español", "Arce rojo", "name")),
            model);

        Assert.That(model.Operations.Single().Target, Is.EqualTo("Name (Español)"));
        _localized.Verify(s => s.SaveLocalizedValueAsync(It.IsAny<Product>(), It.IsAny<Expression<Func<Product, string>>>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    private AkeneoProductLocalizationSynchronizer Synchronizer() => new(_localized.Object, _urls.Object);

    private static AkeneoProductSyncContext Context(params AkeneoLocalizedValue[] values)
    {
        var product = new Product { Id = 100, Name = "Red Maple" };
        return new AkeneoProductSyncContext
        {
            Source = Source(),
            SourceEntityType = AkeneoEntityType.Product,
            Request = new AkeneoProductImportRequest(),
            Result = new AkeneoProductImportResult(),
            ExistingProduct = product,
            Product = product,
            LocalizedValues = values
        };
    }

    #endregion
}
