using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Extensions;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Assets;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public sealed class AkeneoAssetResolver(
    IAkeneoApiClient apiClient,
    IAkeneoProductValueResolver productValueResolver,
    IAkeneoValueTemplateRenderer templateRenderer)
    : IAkeneoAssetResolver
{
    private const int MaximumSourceCacheEntries = 500;

    // Asset records are commonly reused by many products. A bounded, per-sync
    // resolver cache prevents repeatedly loading the same Asset Manager record
    // or media metadata without allowing an unbounded catalog-sized cache.
    private readonly Dictionary<string, AkeneoAssetDefinition> _assetCache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AkeneoMediaFileDefinition> _mediaFileCache =
        new(StringComparer.OrdinalIgnoreCase);
    public async Task<AkeneoAssetResolutionResult> ResolveAsync(
        AkeneoProductSyncContext context,
        AkeneoAssetMapping mapping,
        CancellationToken cancellationToken = default)
    {
        var sourceAttributeCodes = new[]
            {
                mapping.SourceAttributeCode,
                mapping.FallbackSourceAttributeCode
            }
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var sourceAttributeCode in sourceAttributeCodes)
        {
            var sourceResolution = ResolveSourceCodes(
                context,
                sourceAttributeCode);

            // A populated source with no value matching the requested locale or
            // channel is not considered empty. Preserve managed assets instead
            // of silently falling back to a value from another context.
            if (sourceResolution.ContextMismatch)
            {
                return new AkeneoAssetResolutionResult
                {
                    CanReconcile = false,
                    Warning =
                        $"Asset source '{sourceAttributeCode}' has values, but none " +
                        $"matched locale '{context.Request.Locale ?? "(none)"}' and " +
                        $"channel '{context.Request.Channel ?? "(none)"}'. " +
                        "Existing managed assets were preserved."
                };
            }

            // Missing and authoritatively empty sources advance to the fallback.
            if (sourceResolution.SourceCodes.Count == 0)
                continue;

            return await ResolveSourceCodesAsync(
                context,
                mapping,
                sourceAttributeCode,
                sourceResolution.SourceCodes,
                cancellationToken);
        }

        // Every configured source was missing or empty. Reconciliation is safe
        // and ReplaceManaged can remove assets previously owned by this mapping.
        return new AkeneoAssetResolutionResult
        {
            CanReconcile = true,
            Assets = Array.Empty<AkeneoResolvedAsset>()
        };
    }

    private AssetSourceCodeResolution ResolveSourceCodes(
        AkeneoProductSyncContext context,
        string sourceAttributeCode)
    {
        var configuredValues = default(JsonElement);
        var sourceExists = false;

        if (context.Source.Values.ValueKind == JsonValueKind.Object)
        {
            sourceExists = context.Source.Values.TryGetProperty(
                sourceAttributeCode,
                out configuredValues);
        }

        if (!productValueResolver.TryGetValue(
                context.Source,
                sourceAttributeCode,
                out var sourceValue,
                context.Request.Locale,
                context.Request.Channel,
                context.Request.Currency))
        {
            return new AssetSourceCodeResolution
            {
                // Empty/null values are valid fallback conditions. Only report a
                // context mismatch when the source contains meaningful data that
                // the value resolver could not select for this locale/channel.
                ContextMismatch =
                    sourceExists &&
                    ContainsMeaningfulConfiguredValue(configuredValues)
            };
        }

        var sourceCodes = ReadStringValues(sourceValue.RawData)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new AssetSourceCodeResolution
        {
            SourceCodes = sourceCodes
        };
    }

    private async Task<AkeneoAssetResolutionResult> ResolveSourceCodesAsync(
        AkeneoProductSyncContext context,
        AkeneoAssetMapping mapping,
        string resolvedSourceAttributeCode,
        IReadOnlyList<string> sourceCodes,
        CancellationToken cancellationToken)
    {
        var assets = new List<AkeneoResolvedAsset>();
        var baseOrder = Math.Max(0, mapping.DisplayOrder);
        var maximumAssets = mapping.MaxAssets > 0 ? mapping.MaxAssets : 20;

        for (var index = 0;
             index < sourceCodes.Count && assets.Count < maximumAssets;
             index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            AkeneoResolvedAsset resolved;
            if ((AkeneoAssetSourceType)mapping.SourceTypeId ==
                AkeneoAssetSourceType.ProductMediaAttribute)
            {
                resolved = await ResolveProductMediaAsync(
                    context,
                    mapping,
                    resolvedSourceAttributeCode,
                    sourceCodes[index],
                    baseOrder + index,
                    cancellationToken);
            }
            else
            {
                resolved = await ResolveAssetManagerAssetAsync(
                    context,
                    mapping,
                    resolvedSourceAttributeCode,
                    sourceCodes[index],
                    baseOrder + index,
                    cancellationToken);
            }

            if (resolved != null)
                assets.Add(resolved);
        }

        return new AkeneoAssetResolutionResult
        {
            CanReconcile = true,
            Assets = assets
                .OrderBy(asset => asset.DisplayOrder)
                .ThenBy(
                    asset => asset.AssetCode ?? asset.MediaFileCode,
                    StringComparer.OrdinalIgnoreCase)
                .Take(maximumAssets)
                .ToList()
        };
    }

    private async Task<AkeneoResolvedAsset> ResolveProductMediaAsync(
        AkeneoProductSyncContext context,
        AkeneoAssetMapping mapping,
        string resolvedSourceAttributeCode,
        string mediaFileCode,
        int displayOrder,
        CancellationToken cancellationToken)
    {
        var metadata = await GetProductMediaFileCachedAsync(
            mediaFileCode,
            cancellationToken);

        var fileName = metadata?.OriginalFilename ?? Path.GetFileName(mediaFileCode);
        var mimeType = metadata?.MimeType;
        var templateSource = BuildTemplateSource(
            context.Source,
            null,
            mediaFileCode,
            fileName,
            null);

        return BuildResolvedAsset(
            context,
            mapping,
            templateSource,
            resolvedSourceAttributeCode,
            sourceIdentity: $"product-media|{resolvedSourceAttributeCode}|{mediaFileCode}",
            sourceVersion: $"{mediaFileCode}|{metadata?.Size}|{metadata?.MimeType}|{metadata?.OriginalFilename}",
            assetCode: null,
            mediaFileCode: mediaFileCode,
            externalUrl: null,
            mimeType: mimeType,
            originalFileName: fileName,
            sourceUpdatedOnUtc: null,
            displayOrder: displayOrder,
            downloadUrl: null);
    }

    private async Task<AkeneoResolvedAsset> ResolveAssetManagerAssetAsync(
        AkeneoProductSyncContext context,
        AkeneoAssetMapping mapping,
        string resolvedSourceAttributeCode,
        string assetCode,
        int defaultDisplayOrder,
        CancellationToken cancellationToken)
    {
        var familyCode = mapping.AssetFamilyCode?.Trim();
        if (string.IsNullOrWhiteSpace(familyCode))
            return null;

        var asset = await GetAssetCachedAsync(
            familyCode,
            assetCode,
            cancellationToken);

        if (asset == null || asset.Values.ValueKind != JsonValueKind.Object)
            return null;

        if (!RoleMatches(asset.Values, mapping, context))
            return null;

        if (!TryGetAssetValue(
                asset.Values,
                mapping.AssetMediaAttributeCode,
                context.Request.Locale,
                context.Request.Channel,
                out var mediaValue))
        {
            return null;
        }

        var data = mediaValue.TryGetProperty(
            "data",
            out var dataElement)
            ? ReadSingleString(dataElement)
            : null;

        var linkedData = mediaValue.TryGetProperty(
            "linked_data",
            out var linked)
            ? linked
            : default;

        var downloadUrl = TryGetNestedString(
            mediaValue,
            "_links",
            "download",
            "href");

        var externalUrl =
            GetString(linkedData, "full_url") ??
            (LooksLikeAbsoluteUrl(data) ? data : null);

        var mediaFileCode =
            externalUrl == null
                ? data
                : null;
        var mimeType = GetString(linkedData, "mime_type");
        var originalFileName = GetString(linkedData, "original_filename") ??
            (externalUrl != null ? Path.GetFileName(new Uri(externalUrl).LocalPath) : Path.GetFileName(mediaFileCode));
        var updated = ParseDate(GetString(linkedData, "updated_at")) ?? asset.UpdatedOnUtc;
        var displayOrder = ResolveAssetDisplayOrder(asset.Values, mapping, context, defaultDisplayOrder);
        var templateSource = BuildTemplateSource(
            context.Source,
            asset.Values,
            asset.Code,
            originalFileName,
            externalUrl ?? mediaFileCode);

        return BuildResolvedAsset(
            context,
            mapping,
            templateSource,
            resolvedSourceAttributeCode,
            sourceIdentity:
            $"asset|{resolvedSourceAttributeCode}|{familyCode}|{asset.Code}|" +
            $"{mapping.AssetMediaAttributeCode}",
            sourceVersion:
            $"{asset.Code}|{mediaFileCode}|{downloadUrl}|" +
            $"{externalUrl}|{mimeType}|{originalFileName}|{updated:O}",
            assetCode: asset.Code,
            mediaFileCode: mediaFileCode,
            downloadUrl: downloadUrl,
            externalUrl: externalUrl,
            mimeType: mimeType,
            originalFileName: originalFileName,
            sourceUpdatedOnUtc: updated,
            displayOrder: displayOrder);
    }

    private async Task<AkeneoMediaFileDefinition> GetProductMediaFileCachedAsync(
        string mediaFileCode,
        CancellationToken cancellationToken)
    {
        if (_mediaFileCache.TryGetValue(mediaFileCode, out var cached))
            return cached;

        var loaded = await apiClient.GetProductMediaFileAsync(
            mediaFileCode,
            cancellationToken);

        if (loaded != null && _mediaFileCache.Count < MaximumSourceCacheEntries)
            _mediaFileCache[mediaFileCode] = loaded;

        return loaded;
    }

    private async Task<AkeneoAssetDefinition> GetAssetCachedAsync(
        string assetFamilyCode,
        string assetCode,
        CancellationToken cancellationToken)
    {
        var key = $"{assetFamilyCode}|{assetCode}";
        if (_assetCache.TryGetValue(key, out var cached))
            return cached;

        var loaded = await apiClient.GetAssetByCodeAsync(
            assetFamilyCode,
            assetCode,
            cancellationToken);

        if (loaded != null && _assetCache.Count < MaximumSourceCacheEntries)
            _assetCache[key] = loaded;

        return loaded;
    }

    private AkeneoResolvedAsset BuildResolvedAsset(
        AkeneoProductSyncContext context,
        AkeneoAssetMapping mapping,
        AkeneoProductDefinition templateSource,
        string resolvedSourceAttributeCode,
        string sourceIdentity,
        string sourceVersion,
        string assetCode,
        string mediaFileCode,
        string downloadUrl,
        string externalUrl,
        string mimeType,
        string originalFileName,
        DateTime? sourceUpdatedOnUtc,
        int displayOrder)
    {
        var templateContext = new AkeneoValueTemplateContext
        {
            Source = templateSource,
            Locale = context.Request.Locale,
            Channel = context.Request.Channel,
            Currency = context.Request.Currency,
            FamilyCode = context.MappingFamilyCode,
            Sku = context.Sku
        };

        var alt = RenderTemplate(mapping.AltTextTemplate, templateContext, context.Product?.Name);
        var title = RenderTemplate(mapping.TitleTextTemplate, templateContext, alt);
        var seo = RenderTemplate(mapping.SeoFilenameTemplate, templateContext, originalFileName);

        var identityHash = Hash(sourceIdentity);
        // This fingerprint deliberately represents the source media itself.
        // Presentation metadata and display order are compared independently so
        // changing alt text does not trigger another binary download.
        var fingerprint = Hash(sourceVersion);

        return new AkeneoResolvedAsset
        {
            Mapping = mapping,
            SourceIdentityHash = identityHash,
            SourceFingerprint = fingerprint,
            SourceAttributeCode = resolvedSourceAttributeCode,
            AssetFamilyCode = mapping.AssetFamilyCode,
            AssetCode = assetCode,
            MediaFileCode = mediaFileCode,
            DownloadUrl = downloadUrl,
            ExternalUrl = externalUrl,
            MimeType = mimeType,
            OriginalFileName = originalFileName,
            AltText = alt,
            TitleText = title,
            SeoFilename = seo,
            DisplayOrder = displayOrder,
            SourceUpdatedOnUtc = sourceUpdatedOnUtc
        };
    }

    private static bool ContainsMeaningfulConfiguredValue(
        JsonElement configuredValues)
    {
        if (configuredValues.ValueKind != JsonValueKind.Array)
            return configuredValues.ValueKind is not (
                JsonValueKind.Undefined or
                JsonValueKind.Null);

        foreach (var valueObject in configuredValues.EnumerateArray())
        {
            if (valueObject.ValueKind != JsonValueKind.Object ||
                !valueObject.TryGetProperty("data", out var data))
            {
                continue;
            }

            if (HasMeaningfulData(data))
                return true;
        }

        return false;
    }

    private static bool HasMeaningfulData(JsonElement data)
    {
        return data.ValueKind switch
        {
            JsonValueKind.Undefined => false,
            JsonValueKind.Null => false,
            JsonValueKind.String =>
                !string.IsNullOrWhiteSpace(data.GetString()),
            JsonValueKind.Array => data.EnumerateArray().Any(HasMeaningfulData),
            JsonValueKind.Object => data.EnumerateObject()
                .Any(property => HasMeaningfulData(property.Value)),
            _ => true
        };
    }

    private sealed class AssetSourceCodeResolution
    {
        public bool ContextMismatch { get; init; }

        public IReadOnlyList<string> SourceCodes { get; init; } =
            Array.Empty<string>();
    }

    private string RenderTemplate(
        string template,
        AkeneoValueTemplateContext context,
        string fallback)
    {
        if (string.IsNullOrWhiteSpace(template))
            return fallback?.Trim();

        var result = templateRenderer.Render(template, context);
        return result.Success && !string.IsNullOrWhiteSpace(result.Value)
            ? result.Value.Trim()
            : fallback?.Trim();
    }

    private bool RoleMatches(
        JsonElement assetValues,
        AkeneoAssetMapping mapping,
        AkeneoProductSyncContext context)
    {
        var required = mapping.RoleValuesCsv.SplitCsv();
        if (required.Count == 0 || string.IsNullOrWhiteSpace(mapping.RoleAttributeCode))
            return true;

        if (!TryGetAssetValue(
                assetValues,
                mapping.RoleAttributeCode,
                context.Request.Locale,
                context.Request.Channel,
                out var roleValue) ||
            !roleValue.TryGetProperty("data", out var data))
        {
            return false;
        }

        var actual = ReadStringValues(data);
        return actual.Any(value => required.Contains(value, StringComparer.OrdinalIgnoreCase));
    }

    private int ResolveAssetDisplayOrder(
        JsonElement assetValues,
        AkeneoAssetMapping mapping,
        AkeneoProductSyncContext context,
        int fallback)
    {
        if (string.IsNullOrWhiteSpace(mapping.SortOrderAttributeCode) ||
            !TryGetAssetValue(
                assetValues,
                mapping.SortOrderAttributeCode,
                context.Request.Locale,
                context.Request.Channel,
                out var value) ||
            !value.TryGetProperty("data", out var data))
        {
            return fallback;
        }

        return int.TryParse(ReadSingleString(data), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var parsed)
            ? Math.Max(0, mapping.DisplayOrder + parsed)
            : fallback;
    }

    private static bool TryGetAssetValue(
        JsonElement values,
        string attributeCode,
        string locale,
        string channel,
        out JsonElement selected)
    {
        selected = default;
        if (values.ValueKind != JsonValueKind.Object ||
            string.IsNullOrWhiteSpace(attributeCode) ||
            !values.TryGetProperty(attributeCode, out var candidates) ||
            candidates.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var scored = candidates.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object)
            .Select(item => new
            {
                Item = item,
                Score = ScoreContext(item, locale, channel)
            })
            .Where(item => item.Score >= 0)
            .OrderByDescending(item => item.Score)
            .FirstOrDefault();

        if (scored == null)
            return false;

        selected = scored.Item;
        return true;
    }

    private static int ScoreContext(JsonElement value, string locale, string channel)
    {
        var score = 0;
        var actualLocale = GetString(value, "locale");
        var actualChannel = GetString(value, "channel") ?? GetString(value, "scope");

        score += ScoreValue(actualLocale, locale, normalizeLocale: true);
        score += ScoreValue(actualChannel, channel, normalizeLocale: false);
        return score;
    }

    private static int ScoreValue(string actual, string requested, bool normalizeLocale)
    {
        if (string.IsNullOrWhiteSpace(requested))
            return string.IsNullOrWhiteSpace(actual) ? 20 : 1;

        var matches = normalizeLocale
            ? string.Equals(NormalizeLocale(actual), NormalizeLocale(requested), StringComparison.OrdinalIgnoreCase)
            : string.Equals(actual, requested, StringComparison.OrdinalIgnoreCase);

        if (matches)
            return 100;
        return string.IsNullOrWhiteSpace(actual) ? 10 : -1000;
    }

    private static string NormalizeLocale(string value) => value?.Trim().Replace('-', '_');

    private static AkeneoProductDefinition BuildTemplateSource(
        AkeneoProductDefinition product,
        JsonElement? assetValues,
        string assetCode,
        string fileName,
        string source)
    {
        var merged = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        if (product.Values.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in product.Values.EnumerateObject())
                merged[property.Name] = property.Value.Clone();
        }

        if (assetValues.HasValue && assetValues.Value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in assetValues.Value.EnumerateObject())
                merged[$"asset_{property.Name}"] = NormalizeAssetValues(property.Value);
        }

        AddSyntheticValue(merged, "asset_code", assetCode);
        AddSyntheticValue(merged, "asset_file_name", fileName);
        AddSyntheticValue(merged, "asset_source", source);

        return new AkeneoProductDefinition
        {
            Uuid = product.Uuid,
            Identifier = product.Identifier,
            Code = product.Code,
            Family = product.Family,
            FamilyVariant = product.FamilyVariant,
            Parent = product.Parent,
            Enabled = product.Enabled,
            Categories = product.Categories?.ToList() ?? new List<string>(),
            Values = JsonSerializer.SerializeToElement(merged)
        };
    }

    private static JsonElement NormalizeAssetValues(JsonElement values)
    {
        if (values.ValueKind != JsonValueKind.Array)
            return values.Clone();

        var normalized = values.EnumerateArray().Select(item =>
        {
            var dictionary = item.EnumerateObject()
                .ToDictionary(property => property.Name, property => property.Value.Clone());
            if (dictionary.TryGetValue("channel", out var channel) &&
                !dictionary.ContainsKey("scope"))
            {
                dictionary["scope"] = channel;
            }
            return dictionary;
        }).ToList();

        return JsonSerializer.SerializeToElement(normalized);
    }

    private static void AddSyntheticValue(
        IDictionary<string, JsonElement> values,
        string code,
        string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        values[code] = JsonSerializer.SerializeToElement(new[]
        {
            new { locale = (string)null, scope = (string)null, data = value }
        });
    }

    private static string TryGetNestedString(
        JsonElement element,
        params string[] propertyNames)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        var current = element;

        foreach (var propertyName in propertyNames)
        {
            if (current.ValueKind != JsonValueKind.Object ||
                !current.TryGetProperty(propertyName, out current))
            {
                return null;
            }
        }

        return current.ValueKind == JsonValueKind.String
            ? current.GetString()
            : null;
    }
    private static IReadOnlyList<string> ReadStringValues(JsonElement? element)
    {
        if (!element.HasValue)
            return Array.Empty<string>();
        var value = element.Value;
        if (value.ValueKind == JsonValueKind.Array)
            return value.EnumerateArray().Select(ReadSingleString).Where(item => item != null).ToList();
        var single = ReadSingleString(value);
        return single == null ? Array.Empty<string>() : new[] { single };
    }

    private static string ReadSingleString(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => null
    };

    private static string GetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return null;
        }
        return property.GetString();
    }

    private static bool LooksLikeAbsoluteUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static DateTime? ParseDate(string value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out var parsed) ? parsed : null;

    private static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
