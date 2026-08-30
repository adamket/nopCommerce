using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.TestData;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

[TestFixture]
public class AkeneoValueTemplateRendererTests
{
    private AkeneoValueTemplateRenderer _renderer = null!;

    [SetUp]
    public void SetUp() => _renderer = new AkeneoValueTemplateRenderer(new AkeneoProductValueResolver());

    [Test]
    public void Validate_returns_unique_referenced_attribute_codes()
    {
        var result = _renderer.Validate("{product_name} - {root_type} - {product_name}");

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.ReferencedAttributeCodes, Is.EqualTo(new[] { "product_name", "root_type" }));
        });
    }

    [Test]
    public void Validate_reports_unbalanced_or_invalid_expressions()
    {
        var result = _renderer.Validate("{root_type == bare_root ? \"Bare\"}");

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Errors, Is.Not.Empty);
        });
    }

    [Test]
    public void Render_combines_built_in_and_attribute_tokens()
    {
        var product = AkeneoTestData.Product(
            identifier: "RM-5",
            valuesJson: """{"product_name":[{"locale":null,"scope":null,"data":"Red Maple"}]}""");

        var result = _renderer.Render(
            "{sku} - {product_name}",
            Context(product, sku: "RM-5"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.Value, Is.EqualTo("RM-5 - Red Maple"));
        });
    }

    [Test]
    public void Render_uses_raw_code_by_default_for_conditional_comparison()
    {
        var additional = """
        "linked_data": {
          "bare_root": { "labels": { "en_US": "Bare Root" } }
        }
        """;
        var values = "{\"root_type\":[" +
                     AkeneoTestData.ValueEntry("\"bare_root\"", null, null, additional) +
                     "]}";
        var product = AkeneoTestData.Product(valuesJson: values);

        var result = _renderer.Render(
            "{root_type == bare_root ? \"Bare root product\" : \"Potted product\"}",
            Context(product));

        Assert.That(result.Value, Is.EqualTo("Bare root product"));
    }

    [Test]
    public void Render_supports_label_comparison_and_tokens_inside_branches()
    {
        var additional = """
        "linked_data": {
          "bare_root": { "labels": { "en_US": "Bare Root" } }
        }
        """;
        var values = "{\"root_type\":[" +
                     AkeneoTestData.ValueEntry("\"bare_root\"", null, null, additional) +
                     "],\"pack_quantity\":[" +
                     AkeneoTestData.ValueEntry("5") +
                     "]}";
        var product = AkeneoTestData.Product(valuesJson: values);

        var result = _renderer.Render(
            "{root_type:label == \"Bare Root\" ? \"BR {pack_quantity} pack\" : \"Potted\"}",
            Context(product));

        Assert.That(result.Value, Is.EqualTo("BR 5 pack"));
    }

    [Test]
    public void Render_returns_missing_token_and_no_value_when_required_source_is_missing()
    {
        var product = AkeneoTestData.Product();

        var result = _renderer.Render("Name: {product_name}", Context(product));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Value, Is.Null);
            Assert.That(result.MissingTokens, Is.EqualTo(new[] { "product_name" }));
        });
    }

    [Test]
    public void Render_normalizes_horizontal_whitespace_and_punctuation()
    {
        var product = AkeneoTestData.Product(
            valuesJson: """{"product_name":[{"locale":null,"scope":null,"data":"Red Maple"}]}""");

        var result = _renderer.Render("  {product_name}   ,   tree  ", Context(product));

        Assert.That(result.Value, Is.EqualTo("Red Maple, tree"));
    }

    [Test]
    public void Render_family_variant_token_uses_resolved_context_value_for_leaf()
    {
        var product = AkeneoTestData.Product(identifier: "SKU-1");
        product.Parent = "red_maple_potted";
        product.FamilyVariant = null;

        var result = _renderer.Render(
            "{family_variant}",
            new AkeneoValueTemplateContext
            {
                Source = product,
                Locale = "en_US",
                Channel = "ecommerce",
                Currency = "USD",
                FamilyCode = product.Family,
                FamilyVariantCode = "nursery_potted",
                Sku = "SKU-1"
            });

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.Value, Is.EqualTo("nursery_potted"));
        });
    }

    private static AkeneoValueTemplateContext Context(
        AkeneoProductDefinition product,
        string? sku = null) => new()
        {
            Source = product,
            Locale = "en_US",
            Channel = "ecommerce",
            Currency = "USD",
            FamilyCode = product.Family,
            Sku = sku ?? product.Identifier
        };
}
