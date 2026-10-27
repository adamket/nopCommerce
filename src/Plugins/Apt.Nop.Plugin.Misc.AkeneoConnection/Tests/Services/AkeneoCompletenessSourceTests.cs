using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

/// <summary>
/// "Complete (channel and locale)" lets a Published rule read Akeneo completeness
/// instead of the profile filtering on it, so an incomplete product can be withdrawn.
/// </summary>
[TestFixture]
public class AkeneoCompletenessSourceTests
{
    private static readonly AkeneoProductValueResolver Resolver = new();
    private static readonly AkeneoValueTemplateRenderer Renderer = new(Resolver);

    private static AkeneoProductDefinition Product(int? ecommerceEnglish) => new()
    {
        Identifier = "SKU",
        Uuid = "uuid",
        Enabled = true,
        Values = JsonSerializer.SerializeToElement(new Dictionary<string, object>()),
        Completenesses = ecommerceEnglish == null
            ? null
            :
            [
                new AkeneoCompleteness { Scope = "ecommerce", Locale = "en_US", Data = ecommerceEnglish },
                new AkeneoCompleteness { Scope = "ecommerce", Locale = "es_US", Data = 40 },
                new AkeneoCompleteness { Scope = "print", Locale = "en_US", Data = 100 }
            ]
    };

    [TestCase(100, true)]
    [TestCase(90, false)]
    public void Completeness_is_read_for_the_channel_and_locale(int percent, bool expected)
    {
        Assert.That(AkeneoBuiltInSources.IsComplete(Product(percent), "ecommerce", "en_US"), Is.EqualTo(expected));
    }

    [Test]
    public void No_completeness_for_the_channel_and_locale_gives_no_value()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AkeneoBuiltInSources.IsComplete(Product(null), "ecommerce", "en_US"), Is.Null, "product models and unrated products");
            Assert.That(AkeneoBuiltInSources.IsComplete(Product(100), "mobile", "en_US"), Is.Null, "a channel Akeneo did not rate");
            Assert.That(Resolver.TryGetValue(Product(null), "complete", out _, "en_US", "ecommerce"), Is.False);
        });
    }

    [Test]
    public void The_resolver_maps_complete_like_an_attribute()
    {
        Assert.That(Resolver.TryGetValue(Product(100), "complete", out var value, "en_US", "ecommerce"), Is.True);
        Assert.That(value!.DisplayValue, Is.EqualTo("true"));
    }

    [TestCase(100, "true")]
    [TestCase(60, "false")]
    public async Task A_publication_template_can_test_completeness(int percent, string expected)
    {
        var rendered = await Renderer.RenderAsync(
            "{complete == true ? \"true\" : \"false\"}",
            new AkeneoValueTemplateContext { Source = Product(percent), Locale = "en_US", Channel = "ecommerce", Currency = "USD" });

        Assert.That(rendered.Value, Is.EqualTo(expected));
    }

    [Test]
    public void Complete_is_offered_as_a_mapping_source_that_needs_no_attribute_definition()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AkeneoBuiltInSources.Find("complete")?.Labels["en_US"], Is.EqualTo("Complete (channel and locale)"));
            Assert.That(AkeneoMappingSourceCatalog.IsProductField("complete"), Is.True);
        });
    }

    [Test]
    public void Inherited_values_keep_the_products_completeness()
    {
        var parent = new AkeneoProductDefinition { Code = "model", Values = JsonSerializer.SerializeToElement(new Dictionary<string, object>()) };
        var leaf = Product(100);
        leaf.Parent = "model";

        var resolution = new AkeneoProductModelHierarchyResolver().Resolve(
            leaf, [parent], Domain.AkeneoProductModelHierarchyMode.RootProductModel);

        Assert.That(AkeneoBuiltInSources.IsComplete(resolution.EffectiveLeaf, "ecommerce", "en_US"), Is.True);
    }
}
