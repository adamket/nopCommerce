using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Factories;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Assets;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Media;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Media;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

/// <summary>
/// A variant represented as an attribute combination gets the images of its
/// Variant products asset mappings, added to the parent's gallery, linked to
/// the combination and shared between combinations.
/// </summary>
[TestFixture]
public class AkeneoCombinationPictureTests
{
    private const int ParentId = 10;
    private static readonly byte[] Red = [0xFF, 0xD8, 1, 1];
    private static readonly byte[] RedBack = [0xFF, 0xD8, 1, 2];
    private static readonly byte[] Blue = [0xFF, 0xD8, 2, 2];

    private AkeneoAssetMapping _mapping = null!;
    private List<AkeneoResolvedAsset> _assets = null!;
    private FakeManagedAssets _managed = null!;
    private Dictionary<int, Picture> _pictures = null!;
    private Dictionary<int, byte[]> _binaries = null!;
    private List<ProductPicture> _gallery = null!;
    private List<ProductAttributeCombination> _combinations = null!;
    private List<ProductAttributeCombinationPicture> _links = null!;
    private Mock<IPictureService> _pictureService = null!;
    private Mock<IAkeneoExternalAssetDownloader> _downloader = null!;
    private AkeneoProductAssetSynchronizer _synchronizer = null!;
    private Product _parent = null!;

    [SetUp]
    public void SetUp()
    {
        _mapping = new AkeneoAssetMapping
        {
            Id = 1, MappingKey = "variant_images", Name = "Variant images", Enabled = true,
            SourceAttributeCode = "variant_images",
            DestinationTypeId = (int)AkeneoAssetDestinationType.ProductPicture,
            StorageModeId = (int)AkeneoAssetStorageMode.ImportIntoNopCommerce,
            EntityScopeId = (int)AkeneoAttributeMappingEntityScope.VariantProduct
        };
        _assets = [Asset("red", 0), Asset("red-back", 1)];

        var mappings = new Mock<IAkeneoAssetMappingService>();
        mappings.Setup(s => s.GetEffectiveMappingsAsync("shirts")).ReturnsAsync(() => [_mapping]);
        var resolver = new Mock<IAkeneoAssetResolver>();
        resolver.Setup(s => s.ResolveAsync(It.IsAny<AkeneoProductSyncContext>(), It.IsAny<AkeneoAssetMapping>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new AkeneoAssetResolutionResult { CanReconcile = true, Assets = _assets.ToList() });

        _downloader = new Mock<IAkeneoExternalAssetDownloader>();
        _downloader.Setup(s => s.DownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string url, CancellationToken _) => new AkeneoBinaryFile
            {
                Bytes = url.Contains("blue") ? Blue.ToArray() : url.Contains("back") ? RedBack.ToArray() : Red.ToArray(),
                ContentType = "image/jpeg",
                FileName = url[(url.LastIndexOf('/') + 1)..]
            });

        _managed = new FakeManagedAssets();
        _pictures = new Dictionary<int, Picture>();
        _binaries = new Dictionary<int, byte[]>();
        _pictureService = new Mock<IPictureService>();
        _pictureService.Setup(s => s.GetPictureSeNameAsync(It.IsAny<string>())).ReturnsAsync((string name) => name);
        _pictureService.Setup(s => s.GetPictureByIdAsync(It.IsAny<int>()))
            .ReturnsAsync((int id) => _pictures.GetValueOrDefault(id));
        _pictureService.Setup(s => s.LoadPictureBinaryAsync(It.IsAny<Picture>()))
            .ReturnsAsync((Picture picture) => _binaries.GetValueOrDefault(picture.Id));
        _pictureService.Setup(s => s.InsertPictureAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .ReturnsAsync((byte[] bytes, string mime, string seo, string alt, string title, bool _, bool _) =>
            {
                var picture = new Picture { Id = 500 + _pictures.Count + _binaries.Count, MimeType = mime, SeoFilename = seo, AltAttribute = alt, TitleAttribute = title };
                _pictures[picture.Id] = picture;
                _binaries[picture.Id] = bytes;
                return picture;
            });
        _pictureService.Setup(s => s.UpdatePictureAsync(It.IsAny<Picture>())).ReturnsAsync((Picture picture) => picture);
        _pictureService.Setup(s => s.DeletePictureAsync(It.IsAny<Picture>()))
            .Callback((Picture picture) => _pictures.Remove(picture.Id))
            .Returns(Task.CompletedTask);

        _gallery = [];
        var nextRelationId = 700;
        var products = new Mock<IProductService>();
        products.Setup(s => s.GetProductPicturesByProductIdAsync(It.IsAny<int>()))
            .ReturnsAsync((int productId) => _gallery.Where(item => item.ProductId == productId).ToList());
        products.Setup(s => s.GetProductPictureByIdAsync(It.IsAny<int>()))
            .ReturnsAsync((int id) => _gallery.FirstOrDefault(item => item.Id == id));
        products.Setup(s => s.InsertProductPictureAsync(It.IsAny<ProductPicture>()))
            .Callback((ProductPicture relation) => { relation.Id = nextRelationId++; _gallery.Add(relation); })
            .Returns(Task.CompletedTask);
        products.Setup(s => s.DeleteProductPictureAsync(It.IsAny<ProductPicture>()))
            .Callback((ProductPicture relation) => _gallery.Remove(relation))
            .Returns(Task.CompletedTask);

        _combinations =
        [
            new ProductAttributeCombination { Id = 31, ProductId = ParentId, Sku = "SHIRT-RED-S" },
            new ProductAttributeCombination { Id = 32, ProductId = ParentId, Sku = "SHIRT-RED-M" }
        ];
        _links = [];
        var nextLinkId = 900;
        var attributes = new Mock<IProductAttributeService>();
        attributes.Setup(s => s.GetProductAttributeCombinationByIdAsync(It.IsAny<int>()))
            .ReturnsAsync((int id) => _combinations.FirstOrDefault(item => item.Id == id));
        attributes.Setup(s => s.GetAllProductAttributeCombinationsAsync(It.IsAny<int>()))
            .ReturnsAsync((int productId) => _combinations.Where(item => item.ProductId == productId).ToList());
        attributes.Setup(s => s.GetProductAttributeCombinationPicturesAsync(It.IsAny<int>()))
            .ReturnsAsync((int id) => _links.Where(link => link.ProductAttributeCombinationId == id).ToList());
        attributes.Setup(s => s.InsertProductAttributeCombinationPictureAsync(It.IsAny<ProductAttributeCombinationPicture>()))
            .Callback((ProductAttributeCombinationPicture link) => { link.Id = nextLinkId++; _links.Add(link); })
            .Returns(Task.CompletedTask);
        attributes.Setup(s => s.DeleteProductAttributeCombinationPictureAsync(It.IsAny<ProductAttributeCombinationPicture>()))
            .Callback((ProductAttributeCombinationPicture link) => _links.Remove(link))
            .Returns(Task.CompletedTask);

        var genericAttributes = new Mock<IGenericAttributeService>();
        genericAttributes.SetReturnsDefault(Task.FromResult<string>(null!));

        _synchronizer = new AkeneoProductAssetSynchronizer(
            mappings.Object, _managed, resolver.Object, Mock.Of<IAkeneoApiClient>(), _downloader.Object,
            _pictureService.Object, Mock.Of<IVideoService>(), products.Object, genericAttributes.Object, attributes.Object);
        _parent = new Product { Id = ParentId, Name = "Shirt" };
    }

    private AkeneoResolvedAsset Asset(string code, int order, string fingerprint = null) => new()
    {
        Mapping = _mapping,
        SourceIdentityHash = "id-" + code,
        SourceFingerprint = fingerprint ?? "fp-" + code,
        AssetCode = code,
        ExternalUrl = $"https://example.test/{code}.jpg",
        OriginalFileName = code + ".jpg",
        AltText = "Shirt " + code,
        DisplayOrder = order
    };

    private static AkeneoProductSyncContext Leaf(
        string sku,
        int? combinationId,
        AkeneoCollectionSyncMode mode = AkeneoCollectionSyncMode.ReplaceManaged) => new()
    {
        Source = new AkeneoProductDefinition
        {
            Uuid = "uuid-" + sku, Identifier = sku, Family = "shirts", Parent = "shirt_red", Enabled = true,
            Values = JsonSerializer.SerializeToElement(new Dictionary<string, object>())
        },
        SourceEntityType = AkeneoEntityType.Product,
        Request = new AkeneoProductImportRequest
        {
            SyncProfileId = 7, SyncRunRecordId = 20, UpdateExistingProducts = true, AssetSyncMode = mode
        },
        Result = new AkeneoProductImportResult(),
        MappingFamilyCode = "shirts",
        MappingEntityScope = AkeneoAttributeMappingEntityScope.VariantProduct,
        ProductKey = sku,
        Sku = sku,
        ExistingSyncState = combinationId == null ? null : new AkeneoProductSyncState
        {
            NopProductId = ParentId,
            NopProductAttributeCombinationId = combinationId,
            DestinationKindId = (int)AkeneoProductDestinationKind.ProductAttributeCombination
        },
        PreloadedAssetBinaries = new Dictionary<string, AkeneoBinaryFile>()
    };

    private AkeneoProductSyncContext ParentContext(AkeneoCollectionSyncMode mode) => new()
    {
        Source = new AkeneoProductDefinition
        {
            Code = "shirt_red", Family = "shirts",
            Values = JsonSerializer.SerializeToElement(new Dictionary<string, object>())
        },
        SourceEntityType = AkeneoEntityType.ProductModel,
        Request = new AkeneoProductImportRequest { SyncProfileId = 7, SyncRunRecordId = 20, AssetSyncMode = mode },
        Result = new AkeneoProductImportResult(),
        MappingFamilyCode = "shirts",
        MappingEntityScope = AkeneoAttributeMappingEntityScope.ProductModel,
        ExistingProduct = _parent,
        Product = _parent,
        PreloadedAssetBinaries = new Dictionary<string, AkeneoBinaryFile>()
    };

    private List<int> Linked(int combinationId) =>
        _links.Where(link => link.ProductAttributeCombinationId == combinationId).Select(link => link.PictureId).ToList();

    private int PictureOf(string assetCode, int combinationId) =>
        _managed.Rows.Single(row => row.AssetCode == assetCode && row.NopProductAttributeCombinationId == combinationId).NopPictureId!.Value;

    private int Downloads => _downloader.Invocations.Count;
    private int Inserts => _pictureService.Invocations.Count(call => call.Method.Name == nameof(IPictureService.InsertPictureAsync));

    private Task<bool> Sync(int combinationId, AkeneoCollectionSyncMode mode = AkeneoCollectionSyncMode.ReplaceManaged) =>
        _synchronizer.SynchronizeCombinationAsync(
            Leaf(_combinations.First(item => item.Id == combinationId).Sku, combinationId, mode), _parent, combinationId);

    [Test]
    public async Task Every_image_is_linked_to_the_combination_in_the_parent_gallery()
    {
        var changed = await Sync(31);

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.True);
            Assert.That(Downloads, Is.EqualTo(2));
            Assert.That(Linked(31), Is.EqualTo(new[] { PictureOf("red", 31), PictureOf("red-back", 31) }));
            Assert.That(_managed.Rows.All(row => row.NopProductAttributeCombinationId == 31), Is.True);
            Assert.That(_gallery.Select(item => item.DisplayOrder), Is.EquivalentTo(new[]
            {
                AkeneoProductAssetSynchronizer.CombinationPictureDisplayOrderOffset,
                AkeneoProductAssetSynchronizer.CombinationPictureDisplayOrderOffset + 1
            }));
            Assert.That(_combinations[0].PictureId, Is.Null, "the obsolete column is not written");
        });
    }

    [Test]
    public async Task Combinations_with_the_same_images_share_their_pictures()
    {
        await Sync(31);
        await Sync(32);

        Assert.Multiple(() =>
        {
            Assert.That(Downloads, Is.EqualTo(2));
            Assert.That(Inserts, Is.EqualTo(2));
            Assert.That(_gallery, Has.Count.EqualTo(2));
            Assert.That(Linked(32), Is.EqualTo(Linked(31)));
            Assert.That(_managed.Rows, Has.Count.EqualTo(4));
        });
    }

    [Test]
    public async Task An_identical_gallery_picture_is_reused()
    {
        _assets = [Asset("red", 0)];
        _pictures[400] = new Picture { Id = 400 };
        _binaries[400] = Red.ToArray();
        _gallery.Add(new ProductPicture { Id = 401, ProductId = ParentId, PictureId = 400 });

        await Sync(31);

        Assert.Multiple(() =>
        {
            Assert.That(Inserts, Is.Zero);
            Assert.That(Linked(31), Is.EqualTo(new[] { 400 }));
            Assert.That(_managed.Rows.Single().NopProductPictureId, Is.EqualTo(401));
        });
    }

    [Test]
    public async Task Unchanged_images_are_not_downloaded_again()
    {
        await Sync(31);
        var changed = await Sync(31);

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.False);
            Assert.That(Downloads, Is.EqualTo(2));
            Assert.That(_links, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public async Task Replace_managed_unlinks_and_deletes_a_removed_image()
    {
        await Sync(31);
        var back = PictureOf("red-back", 31);
        _assets = [Asset("red", 0)];

        await Sync(31);

        Assert.Multiple(() =>
        {
            Assert.That(Linked(31), Is.EqualTo(new[] { PictureOf("red", 31) }));
            Assert.That(_pictures.ContainsKey(back), Is.False);
            Assert.That(_gallery, Has.Count.EqualTo(1));
            Assert.That(_managed.Rows, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task A_shared_picture_stays_while_another_combination_uses_it()
    {
        await Sync(31);
        await Sync(32);
        var back = PictureOf("red-back", 31);
        _assets = [Asset("red", 0)];

        await Sync(31);

        Assert.Multiple(() =>
        {
            Assert.That(Linked(31), Has.No.Member(back));
            Assert.That(Linked(32), Has.Member(back));
            Assert.That(_pictures.ContainsKey(back), Is.True);
        });
    }

    [Test]
    public async Task A_new_image_version_replaces_the_picture_in_place()
    {
        _assets = [Asset("red", 0)];
        await Sync(31);
        var pictureId = PictureOf("red", 31);
        _assets = [Asset("red", 0, "fp-red-2")];

        await Sync(31);

        Assert.Multiple(() =>
        {
            Assert.That(PictureOf("red", 31), Is.EqualTo(pictureId));
            Assert.That(Inserts, Is.EqualTo(1));
            Assert.That(Downloads, Is.EqualTo(2));
            Assert.That(Linked(31), Is.EqualTo(new[] { pictureId }));
        });
    }

    [Test]
    public async Task Merge_and_replace_managed_keep_a_manual_link()
    {
        _links.Add(new ProductAttributeCombinationPicture { Id = 1, ProductAttributeCombinationId = 31, PictureId = 400 });
        _assets = [];

        await Sync(31, AkeneoCollectionSyncMode.Merge);
        await Sync(31, AkeneoCollectionSyncMode.ReplaceManaged);

        Assert.That(Linked(31), Is.EqualTo(new[] { 400 }));
    }

    [Test]
    public async Task Replace_all_removes_a_manual_link_only_while_a_picture_mapping_applies()
    {
        _links.Add(new ProductAttributeCombinationPicture { Id = 1, ProductAttributeCombinationId = 31, PictureId = 400 });
        _assets = [Asset("red", 0)];
        _mapping.EntityScopeId = (int)AkeneoAttributeMappingEntityScope.ProductModel;

        await Sync(31, AkeneoCollectionSyncMode.ReplaceAll);
        Assert.That(Linked(31), Is.EqualTo(new[] { 400 }), "no mapping applies to variants");

        _mapping.EntityScopeId = (int)AkeneoAttributeMappingEntityScope.VariantProduct;
        await Sync(31, AkeneoCollectionSyncMode.ReplaceAll);
        Assert.That(Linked(31), Is.EqualTo(new[] { PictureOf("red", 31) }));
    }

    [Test]
    public async Task A_disabled_asset_sync_changes_nothing()
    {
        var changed = await Sync(31, AkeneoCollectionSyncMode.Disabled);

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.False);
            Assert.That(_managed.Rows, Is.Empty);
            Assert.That(Downloads, Is.Zero);
        });
    }

    [Test]
    public async Task Releasing_a_deleted_combination_deletes_its_unused_pictures_and_links()
    {
        await Sync(31);

        await _synchronizer.ReleaseCombinationAsync(31);

        Assert.Multiple(() =>
        {
            Assert.That(_managed.Rows, Is.Empty);
            Assert.That(_pictures, Is.Empty);
            Assert.That(_gallery, Is.Empty);
            Assert.That(_links, Is.Empty);
        });
    }

    [Test]
    public async Task A_picture_another_combination_links_by_hand_is_kept()
    {
        _assets = [Asset("red", 0)];
        await Sync(31);
        var red = PictureOf("red", 31);
        _links.Add(new ProductAttributeCombinationPicture { Id = 2, ProductAttributeCombinationId = 32, PictureId = red });

        await _synchronizer.ReleaseCombinationAsync(31);

        Assert.That(_pictures.ContainsKey(red), Is.True);
    }

    [Test]
    public async Task Rows_of_a_combination_deleted_in_the_admin_are_released_on_the_next_sync()
    {
        await Sync(31);
        _combinations.RemoveAt(0);
        _links.RemoveAll(link => link.ProductAttributeCombinationId == 31);
        _assets = [Asset("blue", 0)];

        await Sync(32);

        Assert.Multiple(() =>
        {
            Assert.That(_managed.Rows.Select(row => row.NopProductAttributeCombinationId), Is.EqualTo(new int?[] { 32 }));
            Assert.That(_pictures, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task The_parent_product_sync_leaves_combination_pictures_alone()
    {
        await Sync(31);

        // The parent itself has no active mapping (the mapping is variant-only),
        // so Replace managed and Replace all clean up everything the parent owns.
        foreach (var mode in new[] { AkeneoCollectionSyncMode.ReplaceManaged, AkeneoCollectionSyncMode.ReplaceAll })
            await _synchronizer.SynchronizeAsync(ParentContext(mode));

        Assert.Multiple(() =>
        {
            Assert.That(_managed.Rows, Has.Count.EqualTo(2));
            Assert.That(_pictures, Has.Count.EqualTo(2));
            Assert.That(_gallery, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public async Task Prepare_downloads_the_images_and_skips_versions_already_held()
    {
        var leaf = Leaf("SHIRT-RED-S", 31);
        await _synchronizer.PrepareAsync(leaf);
        Assert.That(leaf.PreloadedAssetBinaries, Has.Count.EqualTo(2));

        await Sync(31);
        var second = Leaf("SHIRT-RED-M", 32);
        await _synchronizer.PrepareAsync(second);
        Assert.That(second.PreloadedAssetBinaries, Is.Empty, "another combination already holds these images");
    }

    [Test]
    public async Task Dry_run_plans_each_combination_picture()
    {
        var model = new AkeneoProductMappingPreviewModel();

        await _synchronizer.PlanAsync(Leaf("SHIRT-RED-S", 31), model);

        var operations = model.Operations.Where(item => item.Area == "Assets").ToList();
        Assert.Multiple(() =>
        {
            Assert.That(operations.Select(item => item.Target),
                Is.EqualTo(new[] { "Combination picture: red.jpg", "Combination picture: red-back.jpg" }));
            Assert.That(operations.All(item => item.ChangeType == AkeneoDryRunOperationType.Add), Is.True);
            Assert.That(Downloads, Is.Zero);
        });
    }

    [Test]
    public void A_variant_picture_mapping_on_a_model_level_attribute_is_warned_about()
    {
        var variants = new[]
        {
            new AkeneoFamilyVariantDefinition
            {
                Code = "shirts_by_color_size",
                VariantAttributeSets =
                [
                    new AkeneoVariantAttributeSet { Level = 1, Axes = ["color"], Attributes = ["color", "variant_images"] },
                    new AkeneoVariantAttributeSet { Level = 2, Axes = ["size"], Attributes = ["size", "sku"] }
                ]
            }
        };
        var modelLevel = new AkeneoAssetMapping
        {
            MappingKey = "gallery", Name = "Gallery", Enabled = true, SourceAttributeCode = "gallery",
            DestinationTypeId = (int)AkeneoAssetDestinationType.ProductPicture,
            EntityScopeId = (int)AkeneoAttributeMappingEntityScope.All
        };

        var warnings = AkeneoAssetMappingModelFactory.BuildCombinationPictureWarnings([_mapping, modelLevel], variants).ToList();

        Assert.That(warnings, Has.Count.EqualTo(1));
        Assert.That(warnings[0], Does.Contain("'Gallery'").And.Contain("shirts_by_color_size"));
    }

    private sealed class FakeManagedAssets : IAkeneoManagedAssetService
    {
        private int _nextId = 1;
        public List<AkeneoManagedAsset> Rows { get; } = [];

        private Task<IList<AkeneoManagedAsset>> Where(Func<AkeneoManagedAsset, bool> predicate) =>
            Task.FromResult<IList<AkeneoManagedAsset>>(Rows.Where(predicate).ToList());

        public Task<IList<AkeneoManagedAsset>> GetByProductAndMappingAsync(int nopProductId, string assetMappingKey) =>
            Where(row => row.NopProductId == nopProductId && row.NopProductAttributeCombinationId == null && row.AssetMappingKey == assetMappingKey);

        public Task<AkeneoManagedAsset> GetByIdentityAsync(int nopProductId, string assetMappingKey, string sourceIdentityHash) =>
            Task.FromResult(Rows.FirstOrDefault(row => row.NopProductId == nopProductId && row.NopProductAttributeCombinationId == null &&
                row.AssetMappingKey == assetMappingKey && row.SourceIdentityHash == sourceIdentityHash));

        public Task<IList<AkeneoManagedAsset>> GetByProductAsync(int nopProductId) =>
            Where(row => row.NopProductId == nopProductId && row.NopProductAttributeCombinationId == null);

        public Task<IList<AkeneoManagedAsset>> GetAllByProductAsync(int nopProductId) =>
            Where(row => row.NopProductId == nopProductId);

        public Task<IList<AkeneoManagedAsset>> GetByCombinationAsync(int combinationId) =>
            Where(row => row.NopProductAttributeCombinationId == combinationId);

        public Task<IList<AkeneoManagedAsset>> GetByPictureAsync(int nopPictureId) =>
            Where(row => row.NopPictureId == nopPictureId);

        public Task InsertAsync(AkeneoManagedAsset asset)
        {
            asset.Id = _nextId++;
            Rows.Add(asset);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(AkeneoManagedAsset asset) => Task.CompletedTask;

        public Task DeleteAsync(AkeneoManagedAsset asset)
        {
            Rows.Remove(asset);
            return Task.CompletedTask;
        }
    }
}
