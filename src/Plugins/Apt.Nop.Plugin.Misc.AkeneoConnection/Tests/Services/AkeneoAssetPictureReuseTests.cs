using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Assets;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Media;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Media;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

/// <summary>
/// An Akeneo picture whose bytes match a picture the product already has (for
/// example the nopCommerce original it was exported from) takes that picture
/// over instead of importing a copy.
/// </summary>
[TestFixture]
public class AkeneoAssetPictureReuseTests
{
    private static readonly byte[] Image = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4];

    private Mock<IPictureService> _pictures = null!;
    private Mock<IProductService> _products = null!;
    private Mock<IAkeneoManagedAssetService> _managed = null!;
    private AkeneoProductAssetSynchronizer _synchronizer = null!;
    private AkeneoProductSyncContext _context = null!;
    private Picture _original = null!;

    [SetUp]
    public void SetUp()
    {
        var mapping = new AkeneoAssetMapping
        {
            Id = 1,
            MappingKey = "images",
            Name = "Images",
            Enabled = true,
            SourceAttributeCode = "images",
            DestinationTypeId = (int)AkeneoAssetDestinationType.ProductPicture,
            StorageModeId = (int)AkeneoAssetStorageMode.ImportIntoNopCommerce,
            EntityScopeId = (int)AkeneoAttributeMappingEntityScope.All
        };
        var asset = new AkeneoResolvedAsset
        {
            Mapping = mapping,
            SourceIdentityHash = "asset-1",
            SourceFingerprint = "fingerprint-1",
            AssetCode = "spruce_1",
            ExternalUrl = "https://example.test/spruce.jpg",
            OriginalFileName = "nop_picture_739.jpg",
            DisplayOrder = 0
        };

        var mappings = new Mock<IAkeneoAssetMappingService>();
        mappings.Setup(s => s.GetEffectiveMappingsAsync("family")).ReturnsAsync([mapping]);
        var resolver = new Mock<IAkeneoAssetResolver>();
        resolver.Setup(s => s.ResolveAsync(It.IsAny<AkeneoProductSyncContext>(), mapping, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AkeneoAssetResolutionResult { CanReconcile = true, Assets = [asset] });
        var downloader = new Mock<IAkeneoExternalAssetDownloader>();
        downloader.Setup(s => s.DownloadAsync(asset.ExternalUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AkeneoBinaryFile { Bytes = Image.ToArray(), ContentType = "image/jpeg", FileName = "nop_picture_739.jpg" });

        _managed = new Mock<IAkeneoManagedAssetService>();
        _managed.SetReturnsDefault(Task.FromResult<IList<AkeneoManagedAsset>>([]));
        _managed.Setup(s => s.InsertAsync(It.IsAny<AkeneoManagedAsset>())).Returns(Task.CompletedTask);

        _original = new Picture { Id = 739, SeoFilename = "colorado-blue-spruce", MimeType = "image/jpg" };
        _pictures = new Mock<IPictureService>();
        _pictures.SetReturnsDefault(Task.FromResult("spruce"));
        _pictures.Setup(s => s.GetPictureByIdAsync(739)).ReturnsAsync(_original);
        _pictures.Setup(s => s.LoadPictureBinaryAsync(_original)).ReturnsAsync(Image.ToArray());
        _pictures.Setup(s => s.UpdatePictureAsync(_original)).ReturnsAsync(_original);

        _products = new Mock<IProductService>();
        _products.Setup(s => s.GetProductPicturesByProductIdAsync(10))
            .ReturnsAsync([new ProductPicture { Id = 736, ProductId = 10, PictureId = 739, DisplayOrder = 0 }]);

        var genericAttributes = new Mock<IGenericAttributeService>();
        genericAttributes.SetReturnsDefault(Task.FromResult<string>(null!));

        _synchronizer = new AkeneoProductAssetSynchronizer(
            mappings.Object, _managed.Object, resolver.Object, Mock.Of<IAkeneoApiClient>(), downloader.Object,
            _pictures.Object, Mock.Of<IVideoService>(), _products.Object, genericAttributes.Object);

        var product = new Product { Id = 10, Name = "Colorado Blue Spruce" };
        _context = new AkeneoProductSyncContext
        {
            Source = new AkeneoProductDefinition
            {
                Uuid = "uuid-1", Identifier = "SKU-1", Family = "family", Enabled = true,
                Values = JsonSerializer.SerializeToElement(new Dictionary<string, object>())
            },
            SourceEntityType = AkeneoEntityType.Product,
            Request = new AkeneoProductImportRequest
            {
                SyncProfileId = 7, SyncRunRecordId = 20, UpdateExistingProducts = true,
                AssetSyncMode = AkeneoCollectionSyncMode.Merge
            },
            Result = new AkeneoProductImportResult(),
            MappingFamilyCode = "family",
            MappingEntityScope = AkeneoAttributeMappingEntityScope.StandaloneProduct,
            ProductKey = "SKU-1",
            Sku = "SKU-1",
            ExistingProduct = product,
            Product = product
        };
    }

    private int Calls(string method) => _pictures.Invocations.Count(call => call.Method.Name == method);

    [Test]
    public async Task An_identical_unmanaged_picture_is_taken_over_instead_of_copied()
    {
        await _synchronizer.SynchronizeAsync(_context);

        Assert.Multiple(() =>
        {
            Assert.That(Calls(nameof(IPictureService.InsertPictureAsync)), Is.Zero);
            Assert.That(_products.Invocations.Count(call => call.Method.Name == nameof(IProductService.InsertProductPictureAsync)), Is.Zero);
            Assert.That(_context.Result.Messages, Has.Some.Contains("Product picture #739"));
        });
        _managed.Verify(s => s.InsertAsync(It.Is<AkeneoManagedAsset>(m => m.NopPictureId == 739 && m.NopProductPictureId == 736)), Times.Once);
    }

    [Test]
    public async Task A_picture_another_asset_already_manages_is_not_taken_over()
    {
        _managed.Setup(s => s.GetByProductAsync(10))
            .ReturnsAsync([new AkeneoManagedAsset { NopProductId = 10, NopPictureId = 739 }]);
        _pictures.Setup(s => s.InsertPictureAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .ReturnsAsync(new Picture { Id = 900 });

        await _synchronizer.SynchronizeAsync(_context);

        Assert.That(Calls(nameof(IPictureService.InsertPictureAsync)), Is.EqualTo(1));
    }
}
