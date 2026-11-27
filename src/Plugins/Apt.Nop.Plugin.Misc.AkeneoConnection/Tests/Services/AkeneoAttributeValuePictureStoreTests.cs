using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Data;
using Nop.Services.Catalog;


namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

[TestFixture]
public class AkeneoAttributeValuePictureStoreTests
{
    private readonly List<ProductAttributeValuePicture> _links = [];
    private readonly List<ProductPicture> _gallery = [];
    private ProductAttributeValue _value = null!;
    private AkeneoAttributeValuePictureStore _store = null!;

    private static IRepository<T> Repository<T>(IList<T> rows) where T : BaseEntity =>
        Mock.Of<IRepository<T>>(repository => repository.Table == rows.AsQueryable());

    [SetUp]
    public void Setup()
    {
        _links.Clear(); _gallery.Clear();
        _value = new ProductAttributeValue { Id = 3, ProductAttributeMappingId = 2  };
        var mapping = new ProductAttributeMapping { Id = 2, ProductId = 1 };
        _links.Add(new ProductAttributeValuePicture { Id = 1, ProductAttributeValueId = 3, PictureId = 11 });
        _links.Add(new ProductAttributeValuePicture { Id = 2, ProductAttributeValueId = 3, PictureId = 12 });
        _gallery.Add(new ProductPicture { Id = 1, ProductId = 1, PictureId = 10, DisplayOrder = 0 });
        _gallery.Add(new ProductPicture { Id = 2, ProductId = 1, PictureId = 11, DisplayOrder = 1 });
        _gallery.Add(new ProductPicture { Id = 3, ProductId = 1, PictureId = 12, DisplayOrder = 2 });
        var attributes = new Mock<IProductAttributeService>();
        attributes.Setup(service => service.GetProductAttributeMappingByIdAsync(2)).ReturnsAsync(mapping);
        attributes.Setup(service => service.GetProductAttributeValuePicturesAsync(3)).ReturnsAsync(() => _links.ToList());
        attributes.Setup(service => service.InsertProductAttributeValuePictureAsync(It.IsAny<ProductAttributeValuePicture>()))
            .Callback<ProductAttributeValuePicture>(picture => _links.Add(picture)).Returns(Task.CompletedTask);
        attributes.Setup(service => service.DeleteProductAttributeValuePicturesAsync(It.IsAny<IList<ProductAttributeValuePicture>>()))
            .Callback<IList<ProductAttributeValuePicture>>(pictures => { foreach (var picture in pictures) _links.Remove(picture); }).Returns(Task.CompletedTask);
        _store = new AkeneoAttributeValuePictureStore(attributes.Object,  Repository(_links),
            Repository(new List<ProductAttributeValue> { _value }), Repository(new List<ProductAttributeMapping> { mapping }),
            Repository(_gallery), Repository(new List<ProductAttributeCombinationPicture>()));
    }

    [Test]
    public async Task Writes_the_platform_picture_representation_without_duplicates()
    {
        await _store.SetPictureIdsAsync(_value, new[] { 10, 12, 12 });
        await _store.SetPictureIdsAsync(_value, new[] { 10, 12 });
        Assert.That(_links.Select(picture => picture.PictureId), Is.EquivalentTo(new[] { 10, 12 }));
        Assert.That(await _store.GetPictureIdsAsync(_value), Is.EqualTo(new[] { 10, 12 }));
        Assert.That(_links.Select(picture => picture.PictureId).Distinct().Count(), Is.EqualTo(_links.Count));
    }

    [Test]
    public async Task Clearing_value_pictures_does_not_delete_gallery_entries()
    {
        await _store.SetPictureIdsAsync(_value, Array.Empty<int>());
        Assert.That(await _store.GetPictureIdsAsync(_value), Is.Empty);
        Assert.That(_links, Is.Empty); Assert.That(_gallery, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task References_on_values_and_other_products_protect_shared_binaries()
    {
        Assert.That(await _store.IsReferencedAsync(11, 1), Is.True);
        _gallery.Add(new ProductPicture { Id = 4, ProductId = 99, PictureId = 20 });
        Assert.That(await _store.IsReferencedAsync(20, 1), Is.True);
        Assert.That(await _store.IsReferencedAsync(999, 1), Is.False);
        Assert.That(await _store.GetProductPictureIdsAsync(1), Does.Contain(2));
    }
}
