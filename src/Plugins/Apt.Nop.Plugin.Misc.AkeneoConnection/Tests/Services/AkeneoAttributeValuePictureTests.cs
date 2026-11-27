using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Assets;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Media;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Media;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

[TestFixture]
public class AkeneoAttributeValuePictureTests
{
    private readonly List<AkeneoManagedAsset> _rows = [];
    private readonly List<ProductPicture> _gallery = [];
    private readonly Dictionary<int, Picture> _pictures = [];
    private readonly Dictionary<int, byte[]> _bytes = [];
    private readonly List<int> _links = [];
    private readonly List<AkeneoResolvedAsset> _assets = [];
    private Mock<IAkeneoAttributeValuePictureStore> _store = null!;
    private Mock<IProductAttributeService> _attributes = null!;
    private Mock<IAkeneoExternalAssetDownloader> _downloader = null!;
    private Mock<IAkeneoAssetMappingService> _mappings = null!;
    private Mock<IAkeneoAssetResolver> _resolver = null!;
    private AkeneoAssetMapping _mapping = null!;
    private ProductAttributeValue _value = null!;
    private AkeneoProductSyncContext _context = null!;
    private AkeneoProductAssetSynchronizer _sync = null!;

    [SetUp]
    public void Setup()
    {
        _rows.Clear(); _gallery.Clear(); _pictures.Clear(); _bytes.Clear(); _links.Clear(); _assets.Clear();
        _mapping = new AkeneoAssetMapping { Id = 1, MappingKey = "design", Name = "Design pictures", Enabled = true,
            SourceAttributeCode = "variant_pictures", TargetProductAttributeId = 9,
            DestinationTypeId = (int)AkeneoAssetDestinationType.ProductAttributeValuePictures,
            StorageModeId = (int)AkeneoAssetStorageMode.ImportIntoNopCommerce,
            EntityScopeId = (int)AkeneoAttributeMappingEntityScope.VariantProduct };
        _value = new ProductAttributeValue { Id = 30, ProductAttributeMappingId = 20, Name = "tribute" };
        _attributes = new Mock<IProductAttributeService>();
        _attributes.Setup(service => service.GetProductAttributeMappingsByProductIdAsync(10))
            .ReturnsAsync([new ProductAttributeMapping { Id = 20, ProductId = 10, ProductAttributeId = 9 }]);
        _attributes.Setup(service => service.GetProductAttributeValuesAsync(20)).ReturnsAsync([_value]);
        _attributes.Setup(service => service.GetProductAttributeValueByIdAsync(30)).ReturnsAsync(_value);
        _store = new Mock<IAkeneoAttributeValuePictureStore>();
        _store.Setup(service => service.GetPictureIdsAsync(_value)).ReturnsAsync(() => _links.ToList());
        _store.Setup(service => service.SetPictureIdsAsync(_value, It.IsAny<IReadOnlyList<int>>()))
            .Callback<ProductAttributeValue, IReadOnlyList<int>>((_, ids) => { _links.Clear(); _links.AddRange(ids); }).Returns(Task.CompletedTask);
        _store.Setup(service => service.GetProductPictureIdsAsync(10))
            .ReturnsAsync(() => _gallery.Where(item => _links.Contains(item.PictureId)).Select(item => item.Id).ToHashSet());
        _store.Setup(service => service.IsReferencedAsync(It.IsAny<int>(), It.IsAny<int?>()))
            .ReturnsAsync((int id, int? _) => _links.Contains(id));
        _mappings = new Mock<IAkeneoAssetMappingService>();
        _mappings.Setup(service => service.GetEffectiveMappingsAsync("commemoratives")).ReturnsAsync(() => new List<AkeneoAssetMapping> { _mapping });
        _resolver = new Mock<IAkeneoAssetResolver>();
        _resolver.Setup(service => service.ResolveAsync(It.IsAny<AkeneoProductSyncContext>(), _mapping, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new AkeneoAssetResolutionResult { CanReconcile = true, Assets = _assets.ToList() });
        var managed = new Mock<IAkeneoManagedAssetService>();
        managed.Setup(service => service.GetAllByProductAsync(10)).ReturnsAsync(() => _rows.ToList());
        managed.Setup(service => service.GetByProductAsync(10)).ReturnsAsync(() => _rows.ToList());
        managed.Setup(service => service.GetByProductAndMappingAsync(10, It.IsAny<string>()))
            .ReturnsAsync((int _, string key) => _rows.Where(row => row.AssetMappingKey == key).ToList());
        managed.Setup(service => service.GetByPictureAsync(It.IsAny<int>()))
            .ReturnsAsync((int id) => _rows.Where(row => row.NopPictureId == id).ToList());
        managed.Setup(service => service.InsertAsync(It.IsAny<AkeneoManagedAsset>()))
            .Callback<AkeneoManagedAsset>(row => { row.Id = _rows.Count + 1; _rows.Add(row); }).Returns(Task.CompletedTask);
        managed.Setup(service => service.DeleteAsync(It.IsAny<AkeneoManagedAsset>()))
            .Callback<AkeneoManagedAsset>(row => _rows.Remove(row)).Returns(Task.CompletedTask);
        var pictures = new Mock<IPictureService>();
        pictures.Setup(service => service.GetPictureByIdAsync(It.IsAny<int>())).ReturnsAsync((int id) => _pictures.GetValueOrDefault(id));
        pictures.Setup(service => service.LoadPictureBinaryAsync(It.IsAny<Picture>())).ReturnsAsync((Picture picture) => _bytes[picture.Id]);
        pictures.SetReturnsDefault(Task.FromResult("picture"));
        pictures.Setup(service => service.InsertPictureAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .ReturnsAsync((byte[] binary, string mime, string seo, string alt, string title, bool _, bool __) => AddPicture(binary, mime, seo, alt, title));
        pictures.Setup(service => service.UpdatePictureAsync(It.IsAny<Picture>())).ReturnsAsync((Picture picture) => picture);
        var products = new Mock<IProductService>();
        products.Setup(service => service.GetProductPicturesByProductIdAsync(10)).ReturnsAsync(() => _gallery.ToList());
        products.Setup(service => service.GetProductPictureByIdAsync(It.IsAny<int>())).ReturnsAsync((int id) => _gallery.FirstOrDefault(item => item.Id == id));
        products.Setup(service => service.InsertProductPictureAsync(It.IsAny<ProductPicture>()))
            .Callback<ProductPicture>(picture => { picture.Id = _gallery.Count + 1; _gallery.Add(picture); }).Returns(Task.CompletedTask);
        products.Setup(service => service.DeleteProductPictureAsync(It.IsAny<ProductPicture>()))
            .Callback<ProductPicture>(picture => _gallery.Remove(picture)).Returns(Task.CompletedTask);
        _downloader = new Mock<IAkeneoExternalAssetDownloader>();
        _downloader.Setup(service => service.DownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string url, CancellationToken _) => new AkeneoBinaryFile { Bytes = System.Text.Encoding.UTF8.GetBytes(url), ContentType = "image/jpeg", FileName = "picture.jpg" });
        _sync = new AkeneoProductAssetSynchronizer(_mappings.Object, managed.Object, _resolver.Object,
            Mock.Of<IAkeneoApiClient>(), _downloader.Object, pictures.Object,
            Mock.Of<IVideoService>(), products.Object, Mock.Of<IGenericAttributeService>(), _attributes.Object, _store.Object,
            new AkeneoValueTemplateRenderer(new AkeneoProductValueResolver()));
        var product = new Product { Id = 10, Name = "Printed card" };
        _context = new AkeneoProductSyncContext
        {
            Product = product, ExistingProduct = product, MappingFamilyCode = "commemoratives", Sku = "8475",
            SourceEntityType = AkeneoEntityType.Product, MappingEntityScope = AkeneoAttributeMappingEntityScope.VariantProduct,
            Source = new AkeneoProductDefinition { Identifier = "8475", Family = "commemoratives", Values = JsonSerializer.SerializeToElement(new { }) },
            Request = new AkeneoProductImportRequest { AssetSyncMode = AkeneoCollectionSyncMode.ReplaceManaged, SyncProfileId = 1 },
            Result = new AkeneoProductImportResult()
        };
        AddAsset("front", 0); AddAsset("inside", 1);
    }

    private Picture AddPicture(byte[] binary, string mime = "image/jpeg", string seo = "picture", string alt = null, string title = null)
    {
        var picture = new Picture { Id = _pictures.Count + 1, MimeType = mime, SeoFilename = seo, AltAttribute = alt, TitleAttribute = title };
        _pictures.Add(picture.Id, picture); _bytes.Add(picture.Id, binary); return picture;
    }
    private void AddAsset(string code, int order) => _assets.Add(new AkeneoResolvedAsset { Mapping = _mapping,
        AssetCode = code, SourceIdentityHash = code, SourceFingerprint = code, DisplayOrder = order,
        ExternalUrl = "https://example.test/" + code + ".jpg", OriginalFileName = code + ".jpg" });

    [Test]
    public async Task Imports_all_images_and_second_run_does_not_duplicate_or_download()
    {
        await _sync.SynchronizeAsync(_context);
        Assert.That(_links, Has.Count.EqualTo(2)); Assert.That(_rows, Has.Count.EqualTo(2));
        Assert.That(_rows.All(row => row.NopProductAttributeValueId == 30 && row.OwnsAttributeValuePicture), Is.True);
        await _sync.SynchronizeAsync(_context);
        Assert.That(_pictures, Has.Count.EqualTo(2)); Assert.That(_gallery, Has.Count.EqualTo(2)); Assert.That(_rows, Has.Count.EqualTo(2));
        _downloader.Verify(service => service.DownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Test]
    public async Task Replacing_an_image_keeps_its_old_picture_when_another_new_asset_uses_it()
    {
        _assets.RemoveAt(1);
        await _sync.SynchronizeAsync(_context);
        var oldPictureId = _links.Single();
        var oldUrl = _assets[0].ExternalUrl;
        _assets.Clear();
        _assets.Add(new AkeneoResolvedAsset { Mapping = _mapping, AssetCode = "new-front", SourceIdentityHash = "front", SourceFingerprint = "new-front", ExternalUrl = "https://example.test/new-front.jpg", DisplayOrder = 0 });
        _assets.Add(new AkeneoResolvedAsset { Mapping = _mapping, AssetCode = "old-front-alias", SourceIdentityHash = "alias", SourceFingerprint = "alias", ExternalUrl = oldUrl, DisplayOrder = 1 });
        await _sync.SynchronizeAsync(_context);
        Assert.That(_links, Has.Count.EqualTo(2));
        Assert.That(_links, Does.Contain(oldPictureId));
        Assert.That(_rows.Select(row => row.NopPictureId), Is.EquivalentTo(_links));
    }

    [Test]
    public async Task Removing_source_image_releases_only_managed_links_and_keeps_gallery()
    {
        await _sync.SynchronizeAsync(_context);
        _links.Add(900); _assets.RemoveAt(0);
        await _sync.SynchronizeAsync(_context);
        Assert.That(_links, Is.EquivalentTo(new[] { 2, 900 })); Assert.That(_rows, Has.Count.EqualTo(1)); Assert.That(_gallery, Has.Count.EqualTo(2));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Existing_associations_are_only_owned_when_adoption_is_enabled(bool adopt)
    {
        _mapping.AdoptExistingValuePictures = adopt;
        var picture = AddPicture(System.Text.Encoding.UTF8.GetBytes(_assets[0].ExternalUrl));
        _gallery.Add(new ProductPicture { Id = 1, PictureId = picture.Id, ProductId = 10 }); _links.Add(picture.Id);
        await _sync.SynchronizeAsync(_context);
        Assert.That(_rows.Single(row => row.NopPictureId == picture.Id).OwnsAttributeValuePicture, Is.EqualTo(adopt));
        _assets.Clear(); await _sync.SynchronizeAsync(_context);
        Assert.That(_links.Contains(picture.Id), Is.EqualTo(!adopt));
    }

    [Test]
    public async Task Failed_download_keeps_all_previous_links_and_ownership()
    {
        await _sync.SynchronizeAsync(_context); var before = _links.ToArray();
        _assets.Clear(); AddAsset("replacement", 0); AddAsset("broken", 1);
        _downloader.Setup(service => service.DownloadAsync(_assets[1].ExternalUrl, It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("Unavailable"));
        await _sync.SynchronizeAsync(_context);
        Assert.That(_links, Is.EqualTo(before)); Assert.That(_rows.Select(row => row.SourceIdentityHash), Is.EquivalentTo(new[] { "front", "inside" }));
    }

    [Test]
    public async Task Ambiguous_target_does_not_download_or_change_links()
    {
        _attributes.Setup(service => service.GetProductAttributeValuesAsync(20)).ReturnsAsync([_value, new ProductAttributeValue { Id = 31 }]);
        _links.Add(900); await _sync.SynchronizeAsync(_context);
        Assert.That(_links, Is.EqualTo(new[] { 900 })); Assert.That(_rows, Is.Empty);
        _downloader.Verify(service => service.DownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestCase("tribute")]
    [TestCase("{design_key}")]
    public async Task Key_template_selects_custom_value_and_dry_run_has_no_writes(string template)
    {
        _mapping.AttributeValueKeyTemplate = template;
        _context.Source.Values = JsonSerializer.SerializeToElement(new { design_key = new[] { new { locale = (string)null!, scope = (string)null!, data = "tribute" } } });
        _attributes.Setup(service => service.GetProductAttributeValuesAsync(20)).ReturnsAsync([_value, new ProductAttributeValue { Id = 31, Name = "other" }]);
        var preview = new AkeneoProductMappingPreviewModel();
        await _sync.PlanAsync(_context, preview);
        Assert.That(preview.Operations.Any(operation => operation.ChangeType == AkeneoDryRunOperationType.Update), Is.True, string.Join(" | ", preview.Operations.Select(operation => operation.Detail)));
        Assert.That(_pictures, Is.Empty); Assert.That(_links, Is.Empty); Assert.That(_rows, Is.Empty);
        await _sync.SynchronizeAsync(_context); Assert.That(_links, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task Replace_all_affects_selected_value_only_and_keeps_its_gallery()
    {
        _context.Request.AssetSyncMode = AkeneoCollectionSyncMode.ReplaceAll;
        _links.Add(900); await _sync.SynchronizeAsync(_context);
        Assert.That(_links, Is.EqualTo(new[] { 1, 2 }));
        _assets.Clear(); await _sync.SynchronizeAsync(_context);
        Assert.That(_links, Is.Empty); Assert.That(_gallery, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task Unresolvable_source_preserves_existing_links()
    {
        await _sync.SynchronizeAsync(_context); var before = _links.ToArray();
        _resolver.Setup(service => service.ResolveAsync(_context, _mapping, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AkeneoAssetResolutionResult { CanReconcile = false });
        await _sync.SynchronizeAsync(_context); Assert.That(_links, Is.EqualTo(before)); Assert.That(_rows, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task Deleting_mapping_releases_owned_links_but_keeps_manual_links()
    {
        await _sync.SynchronizeAsync(_context); _links.Add(900);
        _mappings.Setup(service => service.GetEffectiveMappingsAsync("commemoratives")).ReturnsAsync(new List<AkeneoAssetMapping>());
        await _sync.SynchronizeAsync(_context);
        Assert.That(_links, Is.EqualTo(new[] { 900 })); Assert.That(_rows, Is.Empty); Assert.That(_gallery, Has.Count.EqualTo(2));
    }

    [Test]
    public void Key_matching_uses_custom_value_before_name_and_rejects_duplicates()
    {
        Assert.That(AkeneoAttributeValuePicturePlan.SelectValue(new[] { _value }, "TRIBUTE"), Is.SameAs(_value));
        Assert.Throws<NopException>(() => AkeneoAttributeValuePicturePlan.SelectValue(new[] { _value, _value }, "tribute"));
        Assert.Throws<NopException>(() => AkeneoAttributeValuePicturePlan.SelectValue(Array.Empty<ProductAttributeValue>(), null));
    }
}
