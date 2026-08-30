using Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using NUnit.Framework;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Helpers;

[TestFixture]
public class AkeneoLeafFamilyVariantResolverTests
{
    [Test]
    public void Resolve_UsesImmediateParentFamilyVariant()
    {
        var leaf = new AkeneoProductDefinition
        {
            Identifier = "SKU-1",
            Parent = "red_maple_potted"
        };
        var ancestors = new[]
        {
            new AkeneoProductDefinition
            {
                Code = "red_maple_potted",
                FamilyVariant = "nursery_potted",
                Parent = "red_maple"
            },
            new AkeneoProductDefinition
            {
                Code = "red_maple",
                FamilyVariant = "nursery_potted"
            }
        };

        var resolution = AkeneoLeafFamilyVariantResolver.Resolve(leaf, ancestors);

        Assert.That(resolution.Success, Is.True);
        Assert.That(resolution.FamilyVariantCode, Is.EqualTo("nursery_potted"));
        Assert.That(resolution.ImmediateParentCode, Is.EqualTo("red_maple_potted"));
        Assert.That(resolution.Error, Is.Null);
    }

    [Test]
    public void Resolve_FailsWhenImmediateParentHasNoFamilyVariant()
    {
        var leaf = new AkeneoProductDefinition
        {
            Identifier = "SKU-1",
            Parent = "red_maple_potted"
        };
        var ancestors = new[]
        {
            new AkeneoProductDefinition
            {
                Code = "red_maple_potted"
            }
        };

        var resolution = AkeneoLeafFamilyVariantResolver.Resolve(leaf, ancestors);

        Assert.That(resolution.Success, Is.False);
        Assert.That(resolution.FamilyVariantCode, Is.Null);
        Assert.That(resolution.Error, Does.Contain("will not silently fall back"));
    }

    [Test]
    public void Resolve_StandaloneProductDoesNotRequireFamilyVariant()
    {
        var leaf = new AkeneoProductDefinition
        {
            Identifier = "SKU-1"
        };

        var resolution = AkeneoLeafFamilyVariantResolver.Resolve(
            leaf,
            Array.Empty<AkeneoProductDefinition>());

        Assert.That(resolution.Success, Is.True);
        Assert.That(resolution.FamilyVariantCode, Is.Null);
        Assert.That(resolution.Error, Is.Null);
    }
}
