using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

[TestFixture]
public class AkeneoValueTransformationServiceTests
{
    private AkeneoValueTransformationService _service = null!;

    [SetUp]
    public void SetUp() => _service = new AkeneoValueTransformationService();

    [Test]
    public void Transform_applies_string_rules_distinct_and_join()
    {
        var source = new AkeneoResolvedProductValue
        {
            AttributeCode = "tags",
            DisplayValues = new[] { " maple-tree ", "MAPLE-TREE", "oak-tree" },
            DisplayValue = "maple-tree, MAPLE-TREE, oak-tree"
        };
        var mapping = Mapping("""
        {
          "trim": true,
          "replace": { "from": "-tree", "to": "" },
          "case": "upper",
          "prefix": "[",
          "suffix": "]",
          "distinct": true,
          "join": " | "
        }
        """);

        var result = _service.Transform(source, mapping);

        Assert.Multiple(() =>
        {
            Assert.That(result.Error, Is.Null);
            Assert.That(result.Value!.DisplayValues, Is.EqualTo(new[] { "[MAPLE]", "[OAK]" }));
            Assert.That(result.Value.DisplayValue, Is.EqualTo("[MAPLE] | [OAK]"));
        });
    }

    [Test]
    public void Transform_applies_numeric_rules_in_order()
    {
        var source = new AkeneoResolvedProductValue { DisplayValue = "10.126" };
        var mapping = Mapping("""{"number":{"multiply":2,"add":1,"round":2}}""");

        var result = _service.Transform(source, mapping);

        Assert.That(result.Value!.DisplayValue, Is.EqualTo("21.25"));
    }

    [Test]
    public void Transform_uses_default_when_source_has_no_values()
    {
        var source = new AkeneoResolvedProductValue
        {
            DisplayValue = string.Empty,
            DisplayValues = Array.Empty<string>()
        };
        var mapping = Mapping("""{"default":"Unknown"}""");

        var result = _service.Transform(source, mapping);

        Assert.That(result.Value!.DisplayValue, Is.EqualTo("Unknown"));
    }

    [Test]
    public void Transform_returns_original_value_when_no_rule_exists()
    {
        var source = new AkeneoResolvedProductValue { DisplayValue = "Maple" };

        var result = _service.Transform(source, new AkeneoAttributeMapping());

        Assert.That(result.Value, Is.SameAs(source));
    }

    [Test]
    public void Transform_returns_error_for_invalid_json()
    {
        var result = _service.Transform(
            new AkeneoResolvedProductValue { DisplayValue = "Maple" },
            Mapping("{ invalid"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Value, Is.Null);
            Assert.That(result.Error, Is.Not.Empty);
        });
    }

    [Test]
    public void Transform_rejects_non_object_rule()
    {
        var result = _service.Transform(
            new AkeneoResolvedProductValue { DisplayValue = "Maple" },
            Mapping("[]"));

        Assert.That(result.Error, Is.EqualTo("Transform rule must be a JSON object."));
    }

    private static AkeneoAttributeMapping Mapping(string json) => new()
    {
        TransformRuleJson = json
    };
}
