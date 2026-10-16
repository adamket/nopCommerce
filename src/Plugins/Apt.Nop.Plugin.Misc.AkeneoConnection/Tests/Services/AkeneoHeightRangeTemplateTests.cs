using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

/// <summary>
/// A template can combine two reference-entity number fields into one specification
/// value, e.g. plant_info's mature height min/max into "15' - 25'".
/// </summary>
[TestFixture]
public class AkeneoHeightRangeTemplateTests
{
    private const string Template =
        "{plant_info.mature_height_min_feet}' - {plant_info.mature_height_max_feet}'";

    [TestCase("\"15\"", "\"25\"", "15' - 25'")]
    [TestCase("\"30\"", "\"50\"", "30' - 50'")]
    public async Task RendersTheRange(string min, string max, string expected)
    {
        var result = await RenderAsync(
            $$"""{"mature_height_min_feet":[{"locale":null,"channel":null,"data":{{min}}}],"mature_height_max_feet":[{"locale":null,"channel":null,"data":{{max}}}]}""");

        Assert.That(result.Success, Is.True, string.Join(" ", result.Errors));
        Assert.That(result.Value, Is.EqualTo(expected));
    }

    [Test]
    public async Task AMissingValueRendersNothingRatherThanAHalfRange()
    {
        var result = await RenderAsync(
            """{"mature_height_max_feet":[{"locale":null,"channel":null,"data":"25"}]}""");

        Assert.That(result.Success && !string.IsNullOrWhiteSpace(result.Value), Is.False);
        Assert.That(result.MissingTokens, Is.Not.Empty);
    }

    private static async Task<AkeneoValueTemplateRenderResult> RenderAsync(string recordValues)
    {
        var api = new Mock<IAkeneoApiClient>();
        api.Setup(client => client.GetAttributeByCodeAsync("plant_info", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AkeneoAttributeDefinition
            {
                Code = "plant_info", Type = "akeneo_reference_entity", ReferenceDataName = "plant_info"
            });
        api.Setup(client => client.GetReferenceEntityAttributesAsync("plant_info", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AkeneoReferenceEntityAttributeDefinition>
            {
                new() { Code = "mature_height_min_feet", Type = "number" },
                new() { Code = "mature_height_max_feet", Type = "number" }
            });
        api.Setup(client => client.GetReferenceEntityRecordAsync("plant_info", "african_sumac", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AkeneoReferenceEntityRecordDefinition
            {
                Code = "african_sumac",
                Values = JsonDocument.Parse(recordValues).RootElement.Clone()
            });

        var values = new AkeneoProductValueResolver();
        var renderer = new AkeneoValueTemplateRenderer(
            values,
            new AkeneoReferenceEntityValueResolver(values, api.Object),
            api.Object);

        return await renderer.RenderAsync(
            Template,
            new AkeneoValueTemplateContext
            {
                Locale = "en_US",
                Source = new AkeneoProductDefinition
                {
                    Identifier = "SUMAC-1",
                    Values = JsonDocument.Parse(
                        """{"plant_info":[{"locale":null,"scope":null,"data":"african_sumac","attribute_type":"akeneo_reference_entity"}]}""")
                        .RootElement.Clone()
                }
            });
    }
}
