using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Helpers;

/// <summary>
/// A variant condition can name a select attribute (matched by option code) or a
/// number attribute such as a pack_count axis (matched as a number). The
/// sub-model condition is optional, so a one-level family variant needs none.
/// </summary>
public class AkeneoSubModelRuleMatcherTests
{
    private static AkeneoProductDefinition Item(string attributeCode, object data) => new()
    {
        Identifier = "item",
        Values = JsonSerializer.SerializeToElement(new Dictionary<string, object>
        {
            [attributeCode] = new[] { new { locale = (string)null, scope = (string)null, data } }
        })
    };

    private static AkeneoFamilySubModelRule VariantRule(string attributeCode, string value) => new()
    {
        VariantAxisAttributeCode = attributeCode,
        VariantTriggerValue = value
    };

    [TestCase(10)]
    [TestCase("10")]
    [TestCase("10.0000")]
    public void NumberVariantValueMatchesTheTypedNumber(object data)
    {
        var leaf = Item("pack_count", data);
        var rootModel = Item("name", "Give-a-Tree card");

        Assert.That(AkeneoSubModelRuleMatcher.Matches(VariantRule("pack_count", "10"), leaf, rootModel), Is.True);
        Assert.That(AkeneoSubModelRuleMatcher.Matches(VariantRule("pack_count", "25"), leaf, rootModel), Is.False);
    }

    [Test]
    public void SelectVariantValueMatchesTheOptionCode()
    {
        var leaf = Item("pack_quantity", "10_pack");

        Assert.That(AkeneoSubModelRuleMatcher.Matches(VariantRule("pack_quantity", "10_PACK"), leaf, null), Is.True);
        Assert.That(AkeneoSubModelRuleMatcher.Matches(VariantRule("pack_quantity", "single"), leaf, null), Is.False);
    }

    [Test]
    public void TextIsNotReadAsANumber()
    {
        var leaf = Item("size", "small");

        Assert.That(AkeneoSubModelRuleMatcher.Matches(VariantRule("size", "0"), leaf, null), Is.False);
    }
}
