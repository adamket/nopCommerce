using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Types;

[TestFixture]
public class AkeneoAssociatedValueNameTemplateTests
{
    private static readonly IReadOnlyList<(string AxisCode, string DisplayValue)> Axes =
        new[] { ("root_type", "Bare Root"), ("pack_quantity", "5") };

    [Test]
    public void Render_supports_all_documented_tokens()
    {
        var result = AkeneoAssociatedValueNameTemplate.Render(
            "{axes} | {axis:pack_quantity} | {sku} | {identifier} | {attribute:variant_name}",
            Axes,
            "RM-5",
            "red-maple-5",
            code => code == "variant_name" ? "Red Maple 5 Pack" : null);

        Assert.That(
            result,
            Is.EqualTo("Bare Root / 5 | 5 | RM-5 | red-maple-5 | Red Maple 5 Pack"));
    }

    [Test]
    public void Render_uses_default_axes_template_when_template_is_blank()
    {
        Assert.That(
            AkeneoAssociatedValueNameTemplate.Render(" ", Axes, null, null),
            Is.EqualTo("Bare Root / 5"));
    }

    [Test]
    public void Render_leaves_unknown_tokens_literal()
    {
        Assert.That(
            AkeneoAssociatedValueNameTemplate.Render("Value {unknown}", Axes, null, null),
            Is.EqualTo("Value {unknown}"));
    }

    [TestCase("{axis:}", false)]
    [TestCase("{attribute:}", false)]
    [TestCase("{axes", false)]
    [TestCase("{axes} - {sku}", true)]
    public void TryValidate_detects_invalid_templates(string template, bool expected)
    {
        var valid = AkeneoAssociatedValueNameTemplate.TryValidate(template, out var error);

        Assert.Multiple(() =>
        {
            Assert.That(valid, Is.EqualTo(expected));
            Assert.That(string.IsNullOrWhiteSpace(error), Is.EqualTo(expected));
        });
    }
}
