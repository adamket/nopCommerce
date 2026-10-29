using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Types;

/// <summary>
/// A family's variant display order: up to three sort keys, each a 0-999
/// position, combined first-most-significant into one nopCommerce display order.
/// </summary>
[TestFixture]
public class AkeneoVariantDisplayOrderTests
{
    [Test]
    public void Keys_are_attribute_codes_with_an_optional_record_field()
    {
        Assert.That(AkeneoVariantDisplayOrderKeys.TryParse(" root_type.display_order, container_size\npack_quantity ", out var keys, out _), Is.True);
        Assert.That(keys, Is.EqualTo(new[]
        {
            new AkeneoVariantDisplayOrderKey("root_type", "display_order"),
            new AkeneoVariantDisplayOrderKey("container_size", null),
            new AkeneoVariantDisplayOrderKey("pack_quantity", null)
        }));
        Assert.That(AkeneoVariantDisplayOrderKeys.Parse(null), Is.Empty);
    }

    [TestCase("a, b, c, d", "at most 3")]
    [TestCase("root_type.display.order", "is not a sort key")]
    [TestCase("container size", "is not a sort key")]
    [TestCase("size, size.label", "more than once")]
    public void Invalid_keys_are_rejected(string value, string error)
    {
        Assert.That(AkeneoVariantDisplayOrderKeys.TryParse(value, out _, out var message), Is.False);
        Assert.That(message, Does.Contain(error));
    }

    [Test]
    public void Positions_combine_first_key_most_significant()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AkeneoVariantDisplayOrderKeys.Compose([20, 2, 1]), Is.EqualTo(20_002_001));
            Assert.That(AkeneoVariantDisplayOrderKeys.Compose([10, 999, 999]), Is.LessThan(AkeneoVariantDisplayOrderKeys.Compose([20, 0, 0])));
            Assert.That(AkeneoVariantDisplayOrderKeys.Compose([999, 999, 999]), Is.EqualTo(999_999_999));
            Assert.That(AkeneoVariantDisplayOrderKeys.ToPosition(null), Is.EqualTo(999));
            Assert.That(AkeneoVariantDisplayOrderKeys.ToPosition(-4), Is.EqualTo(0));
            Assert.That(AkeneoVariantDisplayOrderKeys.ToPosition(1500), Is.EqualTo(999));
            Assert.That(AkeneoVariantDisplayOrderKeys.ToPosition(2.5m), Is.EqualTo(3));
        });
    }

    [Test]
    public void Values_are_ranked_0_10_20_by_key_with_unkeyed_values_last()
    {
        var ranked = AkeneoVariantDisplayOrderKeys.Rank(
        [
            (Id: 1, Key: (int?)5_001_000, DisplayOrder: 3),
            (Id: 2, Key: (int?)10, DisplayOrder: 9),
            (Id: 3, Key: (int?)null, DisplayOrder: 1),
            (Id: 4, Key: (int?)0, DisplayOrder: 7),
            (Id: 5, Key: (int?)5_001_000, DisplayOrder: 2)
        ]);

        Assert.That(ranked, Is.EqualTo(new Dictionary<int, int> { [4] = 0, [2] = 10, [5] = 20, [1] = 30, [3] = 40 }));
    }

    [Test]
    public async Task Selects_use_option_order_and_reference_entities_use_the_record_field()
    {
        var api = new Mock<IAkeneoApiClient>();
        api.Setup(s => s.GetAttributeOptionDefinitionsAsync("container_size", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new AkeneoAttributeOptionDefinition { Code = "one_gallon", SortOrder = 1 }, new AkeneoAttributeOptionDefinition { Code = "three_gallon", SortOrder = 2 }]);
        api.Setup(s => s.GetAttributeOptionDefinitionsAsync("pack_quantity", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new AkeneoAttributeOptionDefinition { Code = "pack_1", SortOrder = 1 }, new AkeneoAttributeOptionDefinition { Code = "pack_3", SortOrder = 3 }]);
        var records = new Mock<IAkeneoReferenceEntityValueResolver>();
        records.Setup(s => s.ResolveAsync(It.IsAny<AkeneoProductDefinition>(),
                It.Is<AkeneoAttributeMapping>(m => m.AkeneoAttributeCode == "root_type" && m.AkeneoReferenceEntityAttributeCode == "display_order"),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AkeneoResolvedProductValue { DisplayValue = "20" });
        var values = new FakeValues
        {
            ["root_type"] = Link("potted"),
            ["container_size"] = Select("three_gallon"),
            ["pack_quantity"] = Select("pack_1")
        };

        var order = await new AkeneoVariantDisplayOrderResolver(values, records.Object, api.Object).ResolveAsync(
            new AkeneoProductDefinition(),
            AkeneoVariantDisplayOrderKeys.Parse("root_type.display_order, container_size, pack_quantity"));

        Assert.Multiple(() =>
        {
            Assert.That(order.Value, Is.EqualTo(20_002_001));
            Assert.That(order.PositionsByAttributeCode["container_size"], Is.EqualTo(2));
            Assert.That(order.Warnings, Is.Empty);
        });
    }

    [Test]
    public async Task A_missing_value_sorts_last_with_a_warning()
    {
        var order = await new AkeneoVariantDisplayOrderResolver(
                new FakeValues(),
                Mock.Of<IAkeneoReferenceEntityValueResolver>(),
                Mock.Of<IAkeneoApiClient>())
            .ResolveAsync(new AkeneoProductDefinition(), AkeneoVariantDisplayOrderKeys.Parse("pack_quantity"));

        Assert.That(order.Value, Is.EqualTo(999));
        Assert.That(order.Warnings.Single(), Does.Contain("no 'pack_quantity' value"));
    }

    [Test]
    public async Task A_record_warning_says_whether_the_link_or_the_field_is_missing()
    {
        var resolver = new AkeneoVariantDisplayOrderResolver(
            new FakeValues { ["root_type"] = Link("potted") },
            Mock.Of<IAkeneoReferenceEntityValueResolver>(),
            Mock.Of<IAkeneoApiClient>());

        var noLink = await resolver.ResolveAsync(new AkeneoProductDefinition(), AkeneoVariantDisplayOrderKeys.Parse("root_variant.display_order"));
        var noField = await resolver.ResolveAsync(new AkeneoProductDefinition(), AkeneoVariantDisplayOrderKeys.Parse("root_type.display_order"), "en_US", "ecommerce");

        Assert.Multiple(() =>
        {
            Assert.That(noLink.Warnings.Single(), Does.Contain("the variant has no 'root_variant' value"));
            Assert.That(noField.Warnings.Single(), Does.Contain("record 'potted' linked by 'root_type' has no 'display_order' value for en_US, ecommerce"));
        });
    }

    private static AkeneoResolvedProductValue Link(string recordCode) => new()
    {
        SourceAttributeType = "akeneo_reference_entity",
        ReferenceDataName = "root_type",
        RawData = JsonDocument.Parse($"\"{recordCode}\"").RootElement
    };

    private static AkeneoResolvedProductValue Select(string code) => new()
    {
        SourceAttributeType = "pim_catalog_simpleselect",
        RawData = JsonDocument.Parse($"\"{code}\"").RootElement
    };

    private sealed class FakeValues : Dictionary<string, AkeneoResolvedProductValue>, IAkeneoProductValueResolver
    {
        public string GetValue(
            AkeneoProductDefinition product,
            string attributeCode,
            string locale = null,
            string channel = null,
            string currency = null) =>
            TryGetValue(attributeCode, out var value) ? value.DisplayValue : null;

        public bool TryGetValue(
            AkeneoProductDefinition product,
            string attributeCode,
            out AkeneoResolvedProductValue resolvedValue,
            string locale = null,
            string channel = null,
            string currency = null) =>
            TryGetValue(attributeCode, out resolvedValue);
    }
}
