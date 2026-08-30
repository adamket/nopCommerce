using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

[TestFixture]
public class AkeneoProductModelDeltaFanOutServiceTests
{
    [TestCase(AkeneoRunMode.Full, true, AkeneoProductParentFilterMode.Any, false)]
    [TestCase(AkeneoRunMode.Delta, false, AkeneoProductParentFilterMode.Any, false)]
    [TestCase(AkeneoRunMode.Delta, true, AkeneoProductParentFilterMode.SimpleProductsOnly, false)]
    [TestCase(AkeneoRunMode.Delta, true, AkeneoProductParentFilterMode.Any, true)]
    [TestCase(AkeneoRunMode.Delta, true, AkeneoProductParentFilterMode.VariantProductsOnly, true)]
    public void ShouldRun_requires_a_timestamped_variant_capable_delta(
        AkeneoRunMode runMode,
        bool hasWatermark,
        AkeneoProductParentFilterMode parentMode,
        bool expected)
    {
        var service = CreateService();
        var request = new AkeneoProductBatchImportRequest
        {
            RunMode = runMode,
            UpdatedAfterUtc = hasWatermark ? DateTime.UtcNow : null,
            ProductParentFilterMode = parentMode
        };

        Assert.That(service.ShouldRun(request), Is.EqualTo(expected));
    }

    [Test]
    public void ShouldRun_accepts_a_rolling_days_cutoff_without_a_fixed_timestamp()
    {
        var service = CreateService();
        var request = new AkeneoProductBatchImportRequest
        {
            RunMode = AkeneoRunMode.Delta,
            UpdatedSinceLastNDays = 3,
            ProductParentFilterMode = AkeneoProductParentFilterMode.VariantProductsOnly
        };

        Assert.That(service.ShouldRun(request), Is.True);
    }

    [Test]
    public void BuildDirectProductSearchJson_replaces_linked_asset_timestamp_filter()
    {
        var service = CreateService();
        var request = CreateDeltaRequest();
        request.SearchJson = """{"family":[{"operator":"IN","value":["trees"]}],"updated_including_linked_entities":[{"operator":">","value":"old"}],"updated_including_linked_type":[{"operator":"IN","value":["asset"]}]}""";

        using var document = JsonDocument.Parse(
            service.BuildDirectProductSearchJson(request)!);
        var root = document.RootElement;

        Assert.Multiple(() =>
        {
            Assert.That(root.TryGetProperty("updated", out _), Is.True);
            Assert.That(root.TryGetProperty("updated_including_linked_entities", out _), Is.False);
            Assert.That(root.TryGetProperty("updated_including_linked_type", out _), Is.False);
            Assert.That(root.TryGetProperty("family", out _), Is.True);
        });
    }

    [Test]
    public void BuildDescendantProductSearchJson_removes_delta_filter_and_targets_affected_parents()
    {
        var service = CreateService();
        var request = CreateDeltaRequest();
        request.SearchJson = """{"family":[{"operator":"IN","value":["trees"]}],"parent":[{"operator":"NOT EMPTY"}],"updated":[{"operator":">","value":"old"}]}""";

        using var document = JsonDocument.Parse(
            service.BuildDescendantProductSearchJson(
                request,
                new[] { "root_model", "sub_model" })!);
        var root = document.RootElement;
        var parentCriterion = root.GetProperty("parent")[0];

        Assert.Multiple(() =>
        {
            Assert.That(root.TryGetProperty("updated", out _), Is.False);
            Assert.That(root.TryGetProperty("family", out _), Is.True);
            Assert.That(parentCriterion.GetProperty("operator").GetString(), Is.EqualTo("IN"));
            Assert.That(
                parentCriterion.GetProperty("value")
                    .EnumerateArray()
                    .Select(value => value.GetString()),
                Is.EqualTo(new[] { "root_model", "sub_model" }));
        });
    }

    [Test]
    public async Task BuildPlanAsync_propagates_direct_and_asset_reasons_to_descendant_models()
    {
        var apiClient = new Mock<IAkeneoApiClient>();

        apiClient
            .Setup(client => client.GetProductModelsPageAsync(
                It.IsAny<int>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((int limit, string? searchAfter, string? searchJson, CancellationToken cancellationToken) =>
            {
                _ = limit;
                _ = cancellationToken;
                if (!string.IsNullOrWhiteSpace(searchAfter))
                    return EmptyPage();

                using var document = JsonDocument.Parse(searchJson!);
                var root = document.RootElement;

                if (root.TryGetProperty("updated", out _))
                {
                    return Page(
                        Model("root_direct"));
                }

                if (root.TryGetProperty("updated_including_linked_entities", out _))
                {
                    return Page(
                        Model("root_direct"),
                        Model("root_asset"));
                }

                if (root.TryGetProperty("parent", out var parentCriteria))
                {
                    var parentCodes = parentCriteria[0]
                        .GetProperty("value")
                        .EnumerateArray()
                        .Select(value => value.GetString())
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    if (parentCodes.Contains("root_direct") ||
                        parentCodes.Contains("root_asset"))
                    {
                        return Page(
                            Model("sub_direct", "root_direct"),
                            Model("sub_asset", "root_asset"));
                    }
                }

                return EmptyPage();
            });

        var service = new AkeneoProductModelDeltaFanOutService(apiClient.Object);
        var request = CreateDeltaRequest();
        request.IncludeLinkedAssetUpdates = true;

        var plan = await service.BuildPlanAsync(request);

        Assert.Multiple(() =>
        {
            Assert.That(plan.DirectChangedProductModelCount, Is.EqualTo(1));
            Assert.That(plan.LinkedAssetOnlyProductModelCount, Is.EqualTo(1));
            Assert.That(plan.DescendantProductModelCount, Is.EqualTo(2));
            Assert.That(
                plan.AffectedProductModelReasons["root_direct"],
                Is.EqualTo(AkeneoSyncInclusionReason.AncestorProductModelChange));
            Assert.That(
                plan.AffectedProductModelReasons["sub_direct"],
                Is.EqualTo(AkeneoSyncInclusionReason.AncestorProductModelChange));
            Assert.That(
                plan.AffectedProductModelReasons["root_asset"],
                Is.EqualTo(AkeneoSyncInclusionReason.LinkedAssetChange));
            Assert.That(
                plan.AffectedProductModelReasons["sub_asset"],
                Is.EqualTo(AkeneoSyncInclusionReason.LinkedAssetChange));
        });
    }

    private static AkeneoProductModelDeltaFanOutService CreateService() =>
        new(new Mock<IAkeneoApiClient>().Object);

    private static AkeneoProductBatchImportRequest CreateDeltaRequest() => new()
    {
        RunMode = AkeneoRunMode.Delta,
        PageSize = 100,
        UpdatedAfterUtc = new DateTime(2026, 7, 30, 12, 0, 0, DateTimeKind.Utc),
        ProductParentFilterMode = AkeneoProductParentFilterMode.VariantProductsOnly
    };

    private static AkeneoProductDefinition Model(string code, string? parent = null) => new()
    {
        Code = code,
        Parent = parent
    };

    private static AkeneoProductPageResult Page(params AkeneoProductDefinition[] items) => new()
    {
        Items = items.ToList()
    };

    private static AkeneoProductPageResult EmptyPage() => new();
}
