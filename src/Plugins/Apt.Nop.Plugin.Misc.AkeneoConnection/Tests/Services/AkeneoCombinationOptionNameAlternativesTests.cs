using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Nop.Core;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

[TestFixture]
public class AkeneoCombinationOptionNameAlternativesTests
{
    [TestCase("Custom flag", "Custom flag")]
    [TestCase("", "3 x 5 feet")]
    [TestCase("   ", "3 x 5 feet")]
    [TestCase(null, "3 x 5 feet")]
    public async Task Uses_first_nonempty_alternative(string? variantName, string expected)
    {
        Assert.That(await Render("{variant_name} || {flag_size:label}", variantName), Is.EqualTo(expected));
    }

    [Test]
    public async Task Does_not_use_a_partially_rendered_alternative()
    {
        Assert.That(await Render("Custom {missing_name} || {flag_size:label}"), Is.EqualTo("3 x 5 feet"));
    }

    [Test]
    public async Task Skips_an_empty_conditional_result()
    {
        Assert.That(await Render("{flag_size == 3x5_ft ? \"\" : \"Other\"} || {flag_size:label}"), Is.EqualTo("3 x 5 feet"));
    }

    [Test]
    public async Task Does_not_split_double_bars_in_a_conditional()
    {
        Assert.That(await Render("{flag_size == 3x5_ft ? \"Small || standard\" : \"Large\"} || {flag_size:label}"), Is.EqualTo("Small || standard"));
    }

    [Test]
    public async Task Supports_literal_fallback_and_escaped_braces()
    {
        Assert.That(await Render("{missing_name} || {{Standard}} flag"), Is.EqualTo("{Standard} flag"));
    }

    [TestCase(null)]
    [TestCase(" ")]
    public async Task Blank_template_keeps_axis_display_name(string? template)
    {
        Assert.That(await Render(template), Is.EqualTo("Original axis label"));
    }

    [Test]
    public void Reports_axis_sku_and_missing_tokens_when_all_alternatives_fail()
    {
        var error = Assert.ThrowsAsync<NopException>(async () => await Render("{missing_name} || {other_missing}"));
        Assert.That(error!.Message, Does.Contain("flag_size").And.Contain("FLAG-1").And.Contain("missing_name").And.Contain("other_missing"));
    }

    private static Task<string> Render(string? template, string? variantName = null)
    {
        var values = new Dictionary<string, object>
        {
            ["flag_size"] = new[]
            {
                new
                {
                    locale = (string?)null,
                    scope = (string?)null,
                    data = "3x5_ft",
                    linked_data = new Dictionary<string, object>
                    {
                        ["3x5_ft"] = new { labels = new { en_US = "3 x 5 feet" } }
                    }
                }
            }
        };
        if (variantName != null)
            values["variant_name"] = new[] { new { locale = "en_US", scope = "ecommerce", data = variantName } };

        var context = new AkeneoVariantImportContext
        {
            SourceProduct = new AkeneoProductDefinition { Identifier = "FLAG-1", Values = JsonSerializer.SerializeToElement(values) },
            Sku = "FLAG-1",
            Locale = "en_US",
            Channel = "ecommerce"
        };
        return AkeneoVariantRelationshipService.RenderOptionNameAsync(
            new AkeneoValueTemplateRenderer(new AkeneoProductValueResolver()),
            context,
            new AkeneoFamilyVariantAxisMapping { AkeneoAttributeCode = "flag_size", OptionNameTemplate = template },
            new AkeneoVariantAxisValue { DisplayName = "Original axis label", AkeneoOptionCode = "3x5_ft" });
    }
}
