using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Helpers;

[TestFixture]
public class AkeneoValueContextTests
{
    [Test]
    public void Profile_values_are_trimmed_and_currency_upper_cased()
    {
        var context = AkeneoValueContext.FromProfile(new AkeneoSyncProfile
        {
            AkeneoLocales = " fr_FR ",
            AkeneoChannel = " print ",
            CurrencyCode = " eur "
        });

        Assert.That(context, Is.EqualTo(new AkeneoValueContext("fr_FR", "print", "EUR")));
    }

    [Test]
    public void Missing_profile_or_values_use_the_defaults()
    {
        var expected = new AkeneoValueContext(
            AkeneoValueContext.DefaultLocale,
            AkeneoValueContext.DefaultChannel,
            AkeneoValueContext.DefaultCurrency);

        Assert.Multiple(() =>
        {
            Assert.That(AkeneoValueContext.FromProfile(null), Is.EqualTo(expected));
            Assert.That(AkeneoValueContext.FromProfile(new AkeneoSyncProfile()), Is.EqualTo(expected));
        });
    }

    [Test]
    public void A_legacy_locale_list_resolves_to_its_first_entry()
    {
        Assert.That(AkeneoValueContext.ResolveLocale("es_ES, en_US"), Is.EqualTo("es_ES"));
    }

    [TestCase(0, UnmappedAkeneoAttributeBehavior.Ignore)]
    [TestCase(10, UnmappedAkeneoAttributeBehavior.Log)]
    [TestCase(20, UnmappedAkeneoAttributeBehavior.ImportAsCustomProperty)]
    [TestCase(30, UnmappedAkeneoAttributeBehavior.Log)]
    public void Retired_unmapped_behavior_falls_back_to_logging(int id, UnmappedAkeneoAttributeBehavior expected)
    {
        Assert.That(UnmappedAkeneoAttributeBehaviorExtensions.FromId(id), Is.EqualTo(expected));
    }
}
