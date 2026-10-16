using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

/// <summary>
/// A reference-entity option field (plant_info.soil_preferences) resolves to option labels
/// keyed by option code, never to the codes the record stores.
/// </summary>
[TestFixture]
public class AkeneoReferenceEntityOptionLabelTests
{
    [Test]
    public async Task OptionFieldResolvesToLabelsKeyedByOptionCode()
    {
        var value = await ResolveAsync("soil_preferences", """["loamy", "acidic", "unlabelled"]""");

        // Labels, in record order; the unlabelled option is left out rather than shown as its code.
        Assert.That(value!.DisplayValues, Is.EqualTo(new[] { "Loamy", "Acidic" }));
        Assert.That(
            value.RawData!.Value.EnumerateArray().Select(item => item.GetString()),
            Is.EqualTo(new[] { "loamy", "acidic" }));
    }

    [Test]
    public async Task NonOptionFieldsKeepTheirStoredValues()
    {
        var value = await ResolveAsync("common_name", "\"Red Maple\"", fieldType: "text");

        Assert.That(value!.DisplayValues, Is.EqualTo(new[] { "Red Maple" }));
    }

    private static async Task<AkeneoResolvedProductValue?> ResolveAsync(
        string field, string recordData, string fieldType = "multiple_options")
    {
        var api = new Mock<IAkeneoApiClient>();
        api.Setup(client => client.GetReferenceEntityAttributesAsync("plant_info", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AkeneoReferenceEntityAttributeDefinition>
            {
                new() { Code = field, Type = fieldType }
            });
        api.Setup(client => client.GetReferenceEntityAttributeOptionsAsync("plant_info", field, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AkeneoReferenceEntityAttributeOptionDefinition>
            {
                new() { Code = "acidic", Labels = new() { ["en_US"] = "Acidic" } },
                new() { Code = "loamy", Labels = new() { ["en_US"] = "Loamy" } },
                new() { Code = "unlabelled", Labels = new() }
            });
        api.Setup(client => client.GetReferenceEntityRecordAsync("plant_info", "red_maple", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AkeneoReferenceEntityRecordDefinition
            {
                Code = "red_maple",
                Values = JsonDocument.Parse(
                    $$"""{"{{field}}":[{"locale":null,"channel":null,"data":{{recordData}}}]}""").RootElement.Clone()
            });

        var resolver = new AkeneoReferenceEntityValueResolver(
            new AkeneoProductValueResolver(), api.Object);
        var product = new AkeneoProductDefinition
        {
            Identifier = "1038",
            Values = JsonDocument.Parse(
                """{"plant_info":[{"locale":null,"scope":null,"data":"red_maple","attribute_type":"akeneo_reference_entity"}]}""")
                .RootElement.Clone()
        };

        return await resolver.ResolveAsync(
            product,
            new AkeneoAttributeMapping
            {
                AkeneoAttributeCode = "plant_info",
                AkeneoReferenceEntityCode = "plant_info",
                AkeneoReferenceEntityAttributeCode = field
            },
            locale: "en_US");
    }
}
