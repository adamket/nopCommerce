using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

/// <summary>
/// A delta run re-selects products that link a reference entity record edited since the
/// watermark (e.g. Red Maple's plant_info record), which a product-only delta misses.
/// </summary>
[TestFixture]
public class AkeneoReferenceEntityDeltaFanOutTests
{
    private static readonly DateTime Watermark = new(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task OnlyAttributesWhoseRecordFieldsAnActiveMappingReadsAreWatched()
    {
        var api = new Mock<IAkeneoApiClient>();
        // The mapping leaves plant_info's entity to Akeneo, so it is looked up by code.
        api.Setup(client => client.GetAttributeByCodeAsync("plant_info", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AkeneoAttributeDefinition
            {
                Code = "plant_info", Type = "akeneo_reference_entity", ReferenceDataName = "plant_info"
            });
        api.Setup(client => client.GetUpdatedReferenceEntityRecordCodesAsync("plant_info", Watermark, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "red_maple" });

        var mappings = Mappings(
            // Reads a record field: watched.
            new AkeneoAttributeMapping
            {
                Id = 1, NopTargetTypeId = (int)NopTargetType.SpecificationAttribute,
                AkeneoAttributeCode = "plant_info", AkeneoReferenceEntityAttributeCode = "scientific_name"
            },
            // Template token reading a record field: watched (entity given by the fallback below).
            new AkeneoAttributeMapping
            {
                Id = 2, NopTargetTypeId = (int)NopTargetType.ProductField,
                ValueModeId = (int)AkeneoAttributeMappingValueMode.Template,
                ValueTemplate = "{companion_plants.common_name}"
            },
            // Ignored mapping: not watched.
            new AkeneoAttributeMapping
            {
                Id = 3, NopTargetTypeId = (int)NopTargetType.Ignore,
                AkeneoAttributeCode = "root_variant", AkeneoReferenceEntityCode = "root_types",
                AkeneoReferenceEntityAttributeCode = "label"
            },
            // Record code only: an edit never changes it, so not watched.
            new AkeneoAttributeMapping
            {
                Id = 4, NopTargetTypeId = (int)NopTargetType.CustomProperty,
                AkeneoAttributeCode = "sock_size", AkeneoReferenceEntityCode = "sock_sizes"
            });
        mappings.Setup(service => service.GetAllFallbackSourcesAsync())
            .ReturnsAsync(new List<AkeneoAttributeMappingFallbackSource>
            {
                new()
                {
                    AttributeMappingId = 2, AkeneoAttributeCode = "companion_plants",
                    AkeneoReferenceEntityCode = "plant_info", AkeneoReferenceEntityAttributeCode = "common_name"
                }
            });

        var changes = await new AkeneoProductModelDeltaFanOutService(
                api.Object, mappings.Object, new AkeneoValueTemplateRenderer(new AkeneoProductValueResolver()))
            .FindChangedReferenceEntityRecordsAsync(DeltaRequest());

        Assert.That(
            changes.Select(change => $"{change.AttributeCode}:{string.Join(",", change.RecordCodes)}"),
            Is.EquivalentTo(new[] { "plant_info:red_maple", "companion_plants:red_maple" }));
        // One records search for the one watched entity; no full attribute list.
        api.Verify(client => client.GetUpdatedReferenceEntityRecordCodesAsync(
            It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
        api.Verify(client => client.GetAttributesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task WithNoMappingReadingRecordFieldsTheDeltaMakesNoAkeneoCalls()
    {
        var api = new Mock<IAkeneoApiClient>(MockBehavior.Strict);
        var mappings = Mappings(new AkeneoAttributeMapping
        {
            Id = 1, NopTargetTypeId = (int)NopTargetType.ProductField, AkeneoAttributeCode = "product_name"
        });

        var changes = await new AkeneoProductModelDeltaFanOutService(api.Object, mappings.Object)
            .FindChangedReferenceEntityRecordsAsync(DeltaRequest());

        Assert.That(changes, Is.Empty);
    }

    [Test]
    public async Task NonDeltaRunsDoNotLookForChangedRecords()
    {
        var api = new Mock<IAkeneoApiClient>(MockBehavior.Strict);
        var mappings = new Mock<IAkeneoAttributeMappingService>(MockBehavior.Strict);
        var request = DeltaRequest();
        request.RunMode = AkeneoRunMode.SingleProduct;

        var changes = await new AkeneoProductModelDeltaFanOutService(api.Object, mappings.Object)
            .FindChangedReferenceEntityRecordsAsync(request);

        Assert.That(changes, Is.Empty);
    }

    private static Mock<IAkeneoAttributeMappingService> Mappings(params AkeneoAttributeMapping[] mappings)
    {
        var service = new Mock<IAkeneoAttributeMappingService>();
        service.Setup(item => item.GetAllAkeneoAttributeMappingsAsync()).ReturnsAsync(mappings.ToList());
        service.Setup(item => item.GetAllFallbackSourcesAsync())
            .ReturnsAsync(new List<AkeneoAttributeMappingFallbackSource>());
        return service;
    }

    [Test]
    public void ProductSearchKeepsTheProfileScopeButSelectsByRecordInsteadOfUpdateTime()
    {
        var request = DeltaRequest();
        request.SearchJson =
            """{"family":[{"operator":"IN","value":["nursery"]}],"updated":[{"operator":">","value":"2026-10-15 08:00:00"}]}""";

        var json = new AkeneoProductModelDeltaFanOutService(new Mock<IAkeneoApiClient>().Object)
            .BuildReferenceEntityProductSearchJson(request, "plant_info", new[] { "red_maple" });

        using var search = JsonDocument.Parse(json);
        var root = search.RootElement;
        Assert.That(root.TryGetProperty("updated", out _), Is.False);
        Assert.That(root.GetProperty("family")[0].GetProperty("value")[0].GetString(), Is.EqualTo("nursery"));
        var criterion = root.GetProperty("plant_info")[0];
        Assert.That(criterion.GetProperty("operator").GetString(), Is.EqualTo("IN"));
        Assert.That(criterion.GetProperty("value")[0].GetString(), Is.EqualTo("red_maple"));
    }

    private static AkeneoProductBatchImportRequest DeltaRequest() => new()
    {
        RunMode = AkeneoRunMode.Delta,
        PageSize = 100,
        UpdatedAfterUtc = Watermark
    };
}
