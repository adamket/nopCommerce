using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Helpers;

[TestFixture]
public class AkeneoProductSearchJsonBuilderTests
{
    private AkeneoProductSearchJsonBuilder _builder = null!;

    [SetUp]
    public void SetUp() => _builder = new AkeneoProductSearchJsonBuilder();

    [Test]
    public void Build_returns_null_when_no_filters_are_configured()
    {
        Assert.That(_builder.Build(new AkeneoProductBatchImportRequest()), Is.Null);
    }

    [Test]
    public void Build_normalizes_and_deduplicates_family_group_and_category_codes()
    {
        var request = new AkeneoProductBatchImportRequest
        {
            AkeneoFamilyCodes = new List<string> { " trees ", "TREES", "shrubs" },
            AkeneoProductGroupCodes = new List<string> { " web ", "WEB" },
            AkeneoCategoryCodes = new List<string> { " shade ", "SHADE" },
            CategoryFilterMode = AkeneoCategoryFilterMode.InChildren
        };

        using var document = JsonDocument.Parse(_builder.Build(request)!);
        var root = document.RootElement;

        Assert.Multiple(() =>
        {
            Assert.That(ReadValues(root, "family"), Is.EqualTo(new[] { "trees", "shrubs" }));
            Assert.That(ReadValues(root, "groups"), Is.EqualTo(new[] { "web" }));
            Assert.That(ReadOperator(root, "categories"), Is.EqualTo("IN CHILDREN"));
            Assert.That(ReadValues(root, "categories"), Is.EqualTo(new[] { "shade" }));
        });
    }

    [TestCase(AkeneoCategoryFilterMode.Unclassified, "UNCLASSIFIED")]
    [TestCase(AkeneoCategoryFilterMode.In, "IN")]
    [TestCase(AkeneoCategoryFilterMode.NotIn, "NOT IN")]
    [TestCase(AkeneoCategoryFilterMode.NotInChildren, "NOT IN CHILDREN")]
    [TestCase(AkeneoCategoryFilterMode.InOrUnclassified, "IN OR UNCLASSIFIED")]
    public void Build_uses_expected_category_operator(
        AkeneoCategoryFilterMode mode,
        string expectedOperator)
    {
        var request = new AkeneoProductBatchImportRequest
        {
            CategoryFilterMode = mode,
            AkeneoCategoryCodes = new List<string> { "trees" }
        };

        using var document = JsonDocument.Parse(_builder.Build(request)!);

        Assert.That(ReadOperator(document.RootElement, "categories"), Is.EqualTo(expectedOperator));
    }

    [TestCase(AkeneoProductEnabledFilter.EnabledOnly, true)]
    [TestCase(AkeneoProductEnabledFilter.DisabledOnly, false)]
    public void Build_adds_enabled_filter(AkeneoProductEnabledFilter filter, bool expected)
    {
        var request = new AkeneoProductBatchImportRequest { ProductEnabledFilter = filter };

        using var document = JsonDocument.Parse(_builder.Build(request)!);
        var criterion = document.RootElement.GetProperty("enabled")[0];

        Assert.Multiple(() =>
        {
            Assert.That(criterion.GetProperty("operator").GetString(), Is.EqualTo("="));
            Assert.That(criterion.GetProperty("value").GetBoolean(), Is.EqualTo(expected));
        });
    }

    [Test]
    public void Build_adds_standard_updated_filter_in_utc()
    {
        var request = new AkeneoProductBatchImportRequest
        {
            UpdatedAfterUtc = new DateTime(2026, 7, 1, 12, 30, 0, DateTimeKind.Utc)
        };

        using var document = JsonDocument.Parse(_builder.Build(request)!);
        var criterion = document.RootElement.GetProperty("updated")[0];

        Assert.Multiple(() =>
        {
            Assert.That(criterion.GetProperty("operator").GetString(), Is.EqualTo(">"));
            Assert.That(criterion.GetProperty("value").GetString(), Is.EqualTo("2026-07-01 12:30:00"));
        });
    }

    [Test]
    public void Build_uses_linked_entity_filters_for_asset_updates()
    {
        var request = new AkeneoProductBatchImportRequest
        {
            UpdatedAfterUtc = new DateTime(2026, 7, 1, 12, 30, 0, DateTimeKind.Utc),
            IncludeLinkedAssetUpdates = true
        };

        using var document = JsonDocument.Parse(_builder.Build(request)!);
        var root = document.RootElement;

        Assert.Multiple(() =>
        {
            Assert.That(root.TryGetProperty("updated", out _), Is.False);
            Assert.That(root.GetProperty("updated_including_linked_entities")[0]
                .GetProperty("value").GetString(), Is.EqualTo("2026-07-01 12:30:00"));
            Assert.That(ReadValues(root, "updated_including_linked_type"), Is.EqualTo(new[] { "asset" }));
        });
    }

    [TestCase(AkeneoProductParentFilterMode.SimpleProductsOnly, "EMPTY")]
    [TestCase(AkeneoProductParentFilterMode.VariantProductsOnly, "NOT EMPTY")]
    public void Build_adds_parent_filter(
        AkeneoProductParentFilterMode mode,
        string expectedOperator)
    {
        var request = new AkeneoProductBatchImportRequest { ProductParentFilterMode = mode };

        using var document = JsonDocument.Parse(_builder.Build(request)!);

        Assert.That(ReadOperator(document.RootElement, "parent"), Is.EqualTo(expectedOperator));
    }

    [Test]
    public void Build_appends_additional_array_criteria_without_replacing_generated_criteria()
    {
        var request = new AkeneoProductBatchImportRequest
        {
            AkeneoFamilyCodes = new List<string> { "trees" },
            AdditionalSearchJson = """{"family":[{"operator":"NOT IN","value":["archived"]}],"quality_score":[{"operator":">","value":80}]}"""
        };

        using var document = JsonDocument.Parse(_builder.Build(request)!);

        Assert.Multiple(() =>
        {
            Assert.That(document.RootElement.GetProperty("family").GetArrayLength(), Is.EqualTo(2));
            Assert.That(document.RootElement.GetProperty("quality_score").GetArrayLength(), Is.EqualTo(1));
        });
    }

    [Test]
    public void Build_rejects_non_object_additional_search_json()
    {
        var request = new AkeneoProductBatchImportRequest
        {
            AdditionalSearchJson = "[]"
        };

        Assert.That(
            () => _builder.Build(request),
            Throws.TypeOf<InvalidOperationException>());
    }

    private static string? ReadOperator(JsonElement root, string field) =>
        root.GetProperty(field)[0].GetProperty("operator").GetString();

    private static string[] ReadValues(JsonElement root, string field) =>
        root.GetProperty(field)[0].GetProperty("value")
            .EnumerateArray()
            .Select(value => value.GetString()!)
            .ToArray();
}
