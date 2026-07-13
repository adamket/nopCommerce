namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;
public static class UrlHelper
{
    public static Uri BuildUri(string relativeOrAbsoluteUrl, string baseUrl)
    {
       
        if (string.IsNullOrWhiteSpace(relativeOrAbsoluteUrl))
            throw new ArgumentException(
                "A relative or absolute URL is required.",
                nameof(relativeOrAbsoluteUrl));

        if (!Uri.TryCreate(relativeOrAbsoluteUrl, UriKind.RelativeOrAbsolute, out var uri))
            throw new InvalidOperationException(
                $"Akeneo returned an invalid URL: '{relativeOrAbsoluteUrl}'.");
        var baseUri = new Uri(baseUrl);
        if (!uri.IsAbsoluteUri)
            return new Uri(baseUri, uri);


        if (!IsSameOrigin(baseUri, uri))
        {
            throw new InvalidOperationException(
                $"Akeneo returned a URL outside the configured origin. " +
                $"Configured origin: '{GetOrigin(baseUri)}'. " +
                $"Returned origin: '{GetOrigin(uri)}'.");
        }

        return uri;
    }

    private static bool IsSameOrigin(Uri expected, Uri candidate)
    {
        return string.Equals(
                   expected.Scheme,
                   candidate.Scheme,
                   StringComparison.OrdinalIgnoreCase)
               && string.Equals(
                   expected.IdnHost,
                   candidate.IdnHost,
                   StringComparison.OrdinalIgnoreCase)
               && expected.Port == candidate.Port;
    }

    private static string GetOrigin(Uri uri)
    {
        return uri.GetLeftPart(UriPartial.Authority);
    }
}
