using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Nop.Core.Domain.Catalog;
using Nop.Services.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

/// <summary>
/// After the sync creates or reorders an attribute's values, the value with the
/// lowest display order is preselected, unless a value already is.
/// </summary>
[TestFixture]
public class AkeneoAttributeValuePreselectionTests
{
    private static Mock<IProductAttributeService> Service(params ProductAttributeValue[] values)
    {
        var service = new Mock<IProductAttributeService>();
        service.Setup(s => s.GetProductAttributeValuesAsync(112)).ReturnsAsync(values);
        service.Setup(s => s.UpdateProductAttributeValueAsync(It.IsAny<ProductAttributeValue>())).Returns(Task.CompletedTask);
        return service;
    }

    [Test]
    public void The_lowest_display_order_is_picked_and_ties_go_to_the_lowest_id()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AkeneoAttributeValuePreselection.PickPreselected([(5, 20), (3, 0), (4, 10)]), Is.EqualTo(3));
            Assert.That(AkeneoAttributeValuePreselection.PickPreselected([(9, 0), (7, 0)]), Is.EqualTo(7));
            Assert.That(AkeneoAttributeValuePreselection.PickPreselected([]), Is.Null);
        });
    }

    [Test]
    public async Task With_nothing_preselected_the_lowest_value_is_preselected()
    {
        var bareRoot = new ProductAttributeValue { Id = 682, DisplayOrder = 0 };
        var jumbo = new ProductAttributeValue { Id = 2949, DisplayOrder = 70 };
        var service = Service(jumbo, bareRoot);

        var changed = await AkeneoAttributeValuePreselection.ApplyAsync(service.Object, 112);

        Assert.That((changed, bareRoot.IsPreSelected, jumbo.IsPreSelected), Is.EqualTo((true, true, false)));
        service.Verify(s => s.UpdateProductAttributeValueAsync(bareRoot), Times.Once);
    }

    [Test]
    public async Task An_existing_preselection_is_left_as_it_is()
    {
        var bareRoot = new ProductAttributeValue { Id = 682, DisplayOrder = 0 };
        var potted = new ProductAttributeValue { Id = 684, DisplayOrder = 30, IsPreSelected = true };
        var service = Service(bareRoot, potted);

        var changed = await AkeneoAttributeValuePreselection.ApplyAsync(service.Object, 112);

        Assert.That((changed, bareRoot.IsPreSelected, potted.IsPreSelected), Is.EqualTo((false, false, true)));
        service.Verify(s => s.UpdateProductAttributeValueAsync(It.IsAny<ProductAttributeValue>()), Times.Never);
    }
}
