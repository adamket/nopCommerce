using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Assets;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

[TestFixture]
public class AkeneoAssetResolverAltTextTests
{
    // nop_picture_552 exactly as the Akeneo sandbox returns it (download link trimmed).
    private const string AssetJson = """
        {"code":"nop_picture_552","asset_family_code":"product_pictures","values":{
          "media":[{"locale":null,"channel":null,"data":"e/6/7/f/e67f_nop_picture_552.jpg",
            "linked_data":{"size":255603,"mime_type":"image/jpeg","extension":"jpg",
              "original_filename":"nop_picture_552.jpg","updated_at":"2026-09-17T20:18:13+0000"},
            "attribute_type":"media_file"}],
          "alt_text":[{"locale":"en_US","channel":null,
            "data":"Image of a line of illustrious Red Maple trees","attribute_type":"text"}]},
         "created":"2026-09-17T20:18:13+00:00","updated":"2026-10-14T21:26:25+00:00"}
        """;

    [Test]
    public async Task AssetAltTextToken_UsesTheAssetFamilyAltText()
    {
        var resolved = await ResolveAsync("{asset_alt_text}");

        Assert.That(resolved.AltText, Is.EqualTo("Image of a line of illustrious Red Maple trees"));
    }

    private static async Task<AkeneoResolvedAsset> ResolveAsync(string altTextTemplate)
    {
        var asset = JsonSerializer.Deserialize<AkeneoAssetDefinition>(AssetJson)!;
        var api = new Mock<IAkeneoApiClient>();
        api.Setup(client => client.GetAssetByCodeAsync(
                "product_pictures", "nop_picture_552", It.IsAny<CancellationToken>()))
            .ReturnsAsync(asset);

        var valueResolver = new AkeneoProductValueResolver();
        var resolver = new AkeneoAssetResolver(
            api.Object,
            valueResolver,
            new AkeneoValueTemplateRenderer(valueResolver));

        var source = new AkeneoProductDefinition
        {
            Uuid = "uuid-1038",
            Identifier = "1038",
            Family = "nursery",
            Enabled = true,
            Categories = new List<string>(),
            Values = JsonSerializer.SerializeToElement(new
            {
                product_name = new[] { new { locale = "en_US", scope = (string)null, data = "Red Maple" } },
                product_pictures = new[]
                {
                    new { locale = (string)null, scope = (string)null, data = new[] { "nop_picture_552" } }
                }
            })
        };
        var context = new AkeneoProductSyncContext
        {
            Source = source,
            SourceEntityType = AkeneoEntityType.Product,
            Request = new AkeneoProductImportRequest
            {
                Locale = "en_US",
                Channel = "ecommerce",
                Currency = "USD",
                RunMode = AkeneoRunMode.Delta
            },
            Result = new AkeneoProductImportResult(),
            SourceCode = source.Identifier,
            SourceUuid = source.Uuid,
            ProductKey = source.Uuid,
            Sku = source.Identifier,
            MappingFamilyCode = source.Family,
            MappedValues = Array.Empty<AkeneoResolvedMappedValue>()
        };
        var mapping = new AkeneoAssetMapping
        {
            Name = "Primary images",
            Enabled = true,
            SourceTypeId = (int)AkeneoAssetSourceType.AssetCollection,
            SourceAttributeCode = "product_pictures",
            AssetFamilyCode = "product_pictures",
            AssetMediaAttributeCode = "media",
            DestinationTypeId = (int)AkeneoAssetDestinationType.ProductPicture,
            StorageModeId = (int)AkeneoAssetStorageMode.ImportIntoNopCommerce,
            AltTextTemplate = altTextTemplate
        };

        var result = await resolver.ResolveAsync(context, mapping);

        Assert.That(result.Assets, Has.Count.EqualTo(1), result.Warning);
        return result.Assets[0];
    }
}
