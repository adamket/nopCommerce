using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Extensions;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;

/// <summary>
/// The locale, channel and currency a sync profile reads Akeneo values in, and
/// the one place their defaults live. A profile holds a single locale;
/// <see cref="AkeneoSyncProfile.AkeneoLocales"/> keeps its legacy name, and a
/// value saved as a list resolves to its first entry.
/// </summary>
public sealed record AkeneoValueContext(string Locale, string Channel, string Currency)
{
    public const string DefaultLocale = "en_US";
    public const string DefaultChannel = "ecommerce";
    public const string DefaultCurrency = "USD";

    public static AkeneoValueContext FromProfile(AkeneoSyncProfile profile) => new(
        ResolveLocale(profile?.AkeneoLocales),
        profile?.AkeneoChannel.TrimOr(DefaultChannel) ?? DefaultChannel,
        ResolveCurrency(profile?.CurrencyCode));

    public static string ResolveLocale(string locales) =>
        locales.SplitCsv().FirstOrDefault().TrimOr(DefaultLocale);

    public static string ResolveCurrency(string currency) =>
        currency.TrimOr(DefaultCurrency).ToUpperInvariant();
}
