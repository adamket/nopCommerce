using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Types;

/// <summary>
/// "{attribute:variant_name} || {axes}" names a value from variant_name when it
/// is filled in, and from the axes otherwise.
/// </summary>
[TestFixture]
public class AkeneoAssociatedValueNameAlternativesTests
{
    private static readonly IReadOnlyList<(string AxisCode, string DisplayValue)> Axes =
        new[] { ("root_type", "Bare Root") };

    [Test]
    public void Splits_alternatives_in_order_and_trims_them()
    {
        Assert.That(
            AkeneoAssociatedValueNameTemplate.SplitAlternatives("{attribute:variant_name} || {axes}"),
            Is.EqualTo(new[] { "{attribute:variant_name}", "{axes}" }));
    }

    [Test]
    public void A_template_without_alternatives_is_one_alternative()
    {
        Assert.That(
            AkeneoAssociatedValueNameTemplate.SplitAlternatives("{axes} ({sku})"),
            Is.EqualTo(new[] { "{axes} ({sku})" }));
        Assert.That(
            AkeneoAssociatedValueNameTemplate.SplitAlternatives("  "),
            Is.EqualTo(new[] { AkeneoAssociatedValueNameTemplate.Default }));
    }

    [Test]
    public void Double_bars_inside_an_expression_or_after_a_literal_brace_do_not_split()
    {
        Assert.That(
            AkeneoAssociatedValueNameTemplate.SplitAlternatives("{root_type == potted ? \"a || b\" : \"c\"}"),
            Has.Count.EqualTo(1));
        Assert.That(
            AkeneoAssociatedValueNameTemplate.SplitAlternatives("{{x}} || {axes}"),
            Is.EqualTo(new[] { "{{x}}", "{axes}" }));
    }

    [Test]
    public void An_empty_alternative_is_reported_as_empty()
    {
        Assert.That(
            AkeneoAssociatedValueNameTemplate.SplitAlternatives("{axes} ||"),
            Is.EqualTo(new[] { "{axes}", string.Empty }));
    }

    [TestCase("""{"variant_name":[{"locale":null,"scope":null,"data":"Red Maple 5 Pack"}]}""", "Red Maple 5 Pack")]
    [TestCase("""{"variant_name":[{"locale":null,"scope":null,"data":""}]}""", "Bare Root")]
    [TestCase("""{}""", "Bare Root")]
    public async Task Uses_variant_name_only_when_it_has_a_value(string values, string expected)
    {
        var resolver = new AkeneoProductValueResolver();
        var renderer = new AkeneoValueTemplateRenderer(resolver);
        var context = new AkeneoValueTemplateContext
        {
            Locale = "en_US",
            Source = new AkeneoProductDefinition
            {
                Identifier = "RM-5",
                Values = JsonDocument.Parse(values).RootElement.Clone()
            }
        };

        var name = await AkeneoVariantRelationshipService.RenderAssociatedValueNameAsync(
            renderer,
            "{attribute:variant_name} || {axes}",
            Axes,
            context);

        Assert.That(name, Is.EqualTo(expected));
    }

    [Test]
    public async Task An_axes_only_template_names_the_value_from_the_axes()
    {
        var name = await AkeneoVariantRelationshipService.RenderAssociatedValueNameAsync(
            new AkeneoValueTemplateRenderer(new AkeneoProductValueResolver()),
            AkeneoAssociatedValueNameTemplate.Default,
            Axes,
            new AkeneoValueTemplateContext
            {
                Locale = "en_US",
                Source = new AkeneoProductDefinition
                {
                    Identifier = "RM-5",
                    Values = JsonDocument.Parse("{}").RootElement.Clone()
                }
            });

        Assert.That(name, Is.EqualTo("Bare Root"));
    }
}
