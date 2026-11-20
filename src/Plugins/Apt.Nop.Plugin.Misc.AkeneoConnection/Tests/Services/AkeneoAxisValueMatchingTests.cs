using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Nop.Core.Domain.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

[TestFixture]
public class AkeneoAxisValueMatchingTests
{
    [Test]
    public void Prefers_existing_rendered_label_and_falls_back_to_axis_label()
    {
        var raw = new ProductAttributeValue { Name = "TEN" };
        var rendered = new ProductAttributeValue { Name = "10 count" };
        Assert.That(AkeneoVariantRelationshipService.FindExistingAxisValue(
            [raw, rendered], "10 Count", "ten"), Is.SameAs(rendered));
        Assert.That(AkeneoVariantRelationshipService.FindExistingAxisValue(
            [raw], "10 Count", "ten"), Is.SameAs(raw));
    }
}
