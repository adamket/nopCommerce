using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Nop.Core.Caching;
using Nop.Services.Logging;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public class AkeneoApiClient : IAkeneoApiClient
{
    private const int TokenCacheTimeMinutes = 60 * 24 * 30;
    private const int TokenExpirationBufferMinutes = 1;
    private const int DefaultTokenLifetimeSeconds = 3600;

    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;
    private readonly AkeneoConnectionSettings _settings;
    private readonly IStaticCacheManager _staticCacheManager;

    private readonly SemaphoreSlim _authLock = new(1, 1);

    // Resolved per credentials; never mutates the pooled HttpClient.
    private string _baseUrl = string.Empty;

    private static readonly JsonSerializerOptions SnakeCaseJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public AkeneoApiClient(
        IHttpClientFactory httpClientFactory,
        ILogger logger,
        AkeneoConnectionSettings settings,
        IStaticCacheManager staticCacheManager)
    {
        _logger = logger;
        _settings = settings;
        _staticCacheManager = staticCacheManager;

        // SystemName is "apt.nop.plugin.misc.akeneoconnection"
        _httpClient = httpClientFactory.CreateClient(AkeneoConnectionConstants.SystemName);

        if (!string.IsNullOrWhiteSpace(_settings.AkeneoConnectionBaseUrl))
            Configure(_settings.AkeneoConnectionBaseUrl);
    }

    public void Configure(string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new ArgumentException("Akeneo base URL is required.", nameof(baseUrl));

        // Store only; the HttpClient itself is never modified after construction,
        // so this is safe to call repeatedly even after requests have been sent.
        _baseUrl = baseUrl.TrimEnd('/') + "/";
    }

    #region Public API

    /// <summary>
    /// Sends an unauthenticated request to the configured Akeneo API and
    /// returns true only when Akeneo responds with HTTP 401 Unauthorized.
    /// </summary>
    public async Task<bool> KnockAsync(
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.AkeneoConnectionBaseUrl))
            return false;

        try
        {
            Configure(_settings.AkeneoConnectionBaseUrl);

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                BuildUri("api/rest/v1/channels?limit=1"));

            request.Headers.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));

            request.Headers.UserAgent.ParseAdd(
                "Apt-NopCommerce-AkeneoConnection/1.0 HealthCheck");

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            return response.StatusCode == HttpStatusCode.Unauthorized;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    public async Task<AkeneoConnectionTestResult> TestConnectionAsync(
        AkeneoApiCredentials apiCredentials,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ValidateCredentials(apiCredentials);
            Configure(apiCredentials.BaseUrl);

            var token = await AuthenticateAsync(forceRefresh: true, cancellationToken, apiCredentials, true);
            if (string.IsNullOrEmpty(token))
            {
                return AkeneoConnectionTestResult.FailureResult("Failed to authenticate with Akeneo.");
            }

            var channels = await GetChannelsAsync(limit: 100, cancellationToken, apiCredentials);

            return AkeneoConnectionTestResult.SuccessResult(
                $"Successfully connected to Akeneo with entered credentials. Found {channels.Count} channel(s).");
        }
        catch (Exception ex)
        {
            await _logger.ErrorAsync("Akeneo connection test failed.", ex);

            return AkeneoConnectionTestResult.FailureResult(
                $"Akeneo connection failed: {ex.Message}");
        }
    }

    public async Task<IReadOnlyList<AkeneoProductDefinition>> GetProductsAsync(
        string searchJson = null,
        int limit = 100,
        CancellationToken cancellationToken = default,
        AkeneoApiCredentials apiCredentials = null)
    {
        var query = new Dictionary<string, string?>
        {
            ["pagination_type"] = "search_after",
            ["limit"] = limit.ToString(),
            ["with_attribute_options"] = "true"
        };

        if (!string.IsNullOrWhiteSpace(searchJson))
            query["search"] = searchJson;

        return await GetPagedCollectionAsync<AkeneoProductDefinition>(
            "api/rest/v1/products-uuid", query, cancellationToken, apiCredentials);
    }

    public async Task<AkeneoProductDefinition> GetProductModelByCodeAsync(
        string code,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Product model code is required.", nameof(code));

        return await GetObjectOrNullAsync<AkeneoProductDefinition>(
            $"api/rest/v1/product-models/{Uri.EscapeDataString(code)}?with_attribute_options=true",
            cancellationToken);
    }

    public async Task<AkeneoProductDefinition> GetProductByUuidAsync(
        string uuid,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(uuid))
            throw new ArgumentException("Product UUID is required.", nameof(uuid));

        return await GetObjectOrNullAsync<AkeneoProductDefinition>(
            $"api/rest/v1/products-uuid/{Uri.EscapeDataString(uuid)}?with_attribute_options=true",
            cancellationToken);
    }

    public async Task<IReadOnlyList<AkeneoProductDefinition>> GetChangedProductsAsync(
        DateTime updatedSinceUtc,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var searchJson = BuildUpdatedSinceSearchJson(updatedSinceUtc);
        return await GetProductsAsync(searchJson, limit, cancellationToken);
    }

    public async Task<IReadOnlyList<AkeneoCategoryDefinition>> GetCategoriesAsync(
        int limit = 100,
        CancellationToken cancellationToken = default)
        => await GetSimpleCollectionAsync<AkeneoCategoryDefinition>(
            "api/rest/v1/categories",
            limit,
            cancellationToken);

    public async Task<IReadOnlyList<AkeneoAttributeDefinition>> GetAttributesAsync(
        int limit = 100, CancellationToken cancellationToken = default)
        => await GetSimpleCollectionAsync<AkeneoAttributeDefinition>("api/rest/v1/attributes", limit, cancellationToken);

    public async Task<IReadOnlyList<JsonElement>> GetLocalesAsync(
        int limit = 100, CancellationToken cancellationToken = default)
        => await GetSimpleCollectionAsync("api/rest/v1/locales", limit, cancellationToken);


    public async Task<IReadOnlyList<AkeneoFamilyDefinition>> GetFamiliesAsync(
        int limit = 100, CancellationToken cancellationToken = default)
        => await GetSimpleCollectionAsync<AkeneoFamilyDefinition>("api/rest/v1/families", limit, cancellationToken);

    public async Task<IReadOnlyList<AkeneoChannelDefinition>> GetChannelsAsync(
        int limit = 100,
        CancellationToken cancellationToken = default,
        AkeneoApiCredentials apiCredentials = null)
        => await GetSimpleCollectionAsync<AkeneoChannelDefinition>("api/rest/v1/channels", limit, cancellationToken, apiCredentials);

    public async Task<IReadOnlyList<JsonElement>> GetAttributeOptionsAsync(
        string attributeCode,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(attributeCode))
            throw new ArgumentException("Attribute code is required.", nameof(attributeCode));

        return await GetSimpleCollectionAsync(
            $"api/rest/v1/attributes/{Uri.EscapeDataString(attributeCode)}/options",
            limit, cancellationToken, null);
    }

    public async Task ClearCachedTokenAsync(AkeneoApiCredentials apiCredentials = null)
    {
        apiCredentials ??= GetApiCredentialsFromSettings();
        await _staticCacheManager.RemoveAsync(CreateTokenCacheKey(apiCredentials));
    }

    //public async Task<IReadOnlyList<AkeneoProductGroupDefinition>> GetProductGroupsAsync(
    //    int limit = 100,
    //    CancellationToken cancellationToken = default)
    //    => await GetSimpleCollectionAsync<AkeneoProductGroupDefinition>(
    //        "api/rest/v1/groups",
    //        limit,
    //        cancellationToken);


    public async Task<AkeneoProductPageResult> GetProductsPageAsync(
        int limit = 100,
        string searchAfter = null,
        string searchJson = null,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
            limit = 100;

        var query = new Dictionary<string, string?>
        {
            ["pagination_type"] = "search_after",
            ["limit"] = limit.ToString(),
            ["with_attribute_options"] = "true"
        };

        if (!string.IsNullOrWhiteSpace(searchAfter))
            query["search_after"] = searchAfter;

        if (!string.IsNullOrWhiteSpace(searchJson))
            query["search"] = searchJson;

        var relativeUrl = "api/rest/v1/products-uuid" + ToQueryString(query);

        using var document = await GetJsonDocumentAsync(
            relativeUrl,
            cancellationToken,
            null);

        var page = document.RootElement.Deserialize<PagedCollection<AkeneoProductDefinition>>(
            SnakeCaseJsonOptions);

        return new AkeneoProductPageResult
        {
            Items = page?.Embedded?.Items ?? new List<AkeneoProductDefinition>(),
            SearchAfter = ExtractQueryStringValue(page?.Links?.Next?.Href, "search_after")
        };
    }

    public async Task<IReadOnlyList<AkeneoFamilyAxis>> GetFamilyVariantAxesAsync(
        string familyCode,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(familyCode))
            throw new ArgumentException("Family code is required.", nameof(familyCode));

        var variants = await GetSimpleCollectionAsync<AkeneoFamilyVariantDefinition>(
            $"api/rest/v1/families/{Uri.EscapeDataString(familyCode)}/variants",
            limit, cancellationToken);

        // A family can define several variants; union their axes across levels, then
        // dedupe by code (keeping the lowest level if one repeats) so the caller gets
        // one entry per axis attribute.
        return variants
            .SelectMany(variant => variant.VariantAttributeSets
                .SelectMany(set => set.Axes
                    .Where(axisCode => !string.IsNullOrWhiteSpace(axisCode))
                    .Select(axisCode => new AkeneoFamilyAxis
                    {
                        AttributeCode = axisCode.Trim(),
                        Level = set.Level
                    })))
            .GroupBy(axis => axis.AttributeCode, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(axis => axis.Level).First())
            .OrderBy(axis => axis.Level)
            .ThenBy(axis => axis.AttributeCode, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    #endregion

    #region HTTP / paging

    private async Task<IReadOnlyList<T>> GetSimpleCollectionAsync<T>(
        string endpoint,
        int limit = 100,
        CancellationToken cancellationToken = default,
        AkeneoApiCredentials apiCredentials = null)
    {
        apiCredentials ??= GetApiCredentialsFromSettings();

        var query = new Dictionary<string, string?>
        {
            ["limit"] = limit.ToString()
        };

        return await GetPagedCollectionAsync<T>(
            endpoint,
            query,
            cancellationToken,
            apiCredentials);
    }


    private async Task<IReadOnlyList<JsonElement>> GetSimpleCollectionAsync(
        string relativeUrl,
        int limit,
        CancellationToken cancellationToken = default,
        AkeneoApiCredentials apiCredentials = null)
    {
        apiCredentials ??= GetApiCredentialsFromSettings();
        var query = new Dictionary<string, string?> { ["limit"] = limit.ToString() };
        return await GetPagedCollectionAsync(relativeUrl, query, cancellationToken, apiCredentials);
    }


    private async Task<IReadOnlyList<T>> GetPagedCollectionAsync<T>(
        string relativeUrl,
        Dictionary<string, string?> query,
        CancellationToken cancellationToken = default,
        AkeneoApiCredentials apiCredentials = null)
    {
        var results = new List<T>();
        var nextUrl = relativeUrl + ToQueryString(query);

        while (!string.IsNullOrWhiteSpace(nextUrl))
        {
            using var document = await GetJsonDocumentAsync(nextUrl, cancellationToken, apiCredentials);

            var page = document.RootElement.Deserialize<PagedCollection<T>>(SnakeCaseJsonOptions);

            if (page?.Embedded?.Items != null)
            {
                results.AddRange(page.Embedded.Items.Where(item => item is not null));
            }

            nextUrl = page?.Links?.Next?.Href;
        }

        return results;
    }

    private async Task<IReadOnlyList<JsonElement>> GetPagedCollectionAsync(
        string relativeUrl,
        Dictionary<string, string?> query,
        CancellationToken cancellationToken = default,
        AkeneoApiCredentials apiCredentials = null)
    {
        var results = new List<JsonElement>();
        string nextUrl = relativeUrl + ToQueryString(query);

        while (!string.IsNullOrWhiteSpace(nextUrl))
        {
            using var document = await GetJsonDocumentAsync(nextUrl, cancellationToken, apiCredentials);
            var root = document.RootElement;

            if (root.TryGetProperty("_embedded", out var embedded) &&
                embedded.TryGetProperty("items", out var items) &&
                items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                    results.Add(item.Clone());
            }

            nextUrl = GetNextPageUrl(root);
        }

        return results;
    }

    private async Task<JsonDocument> GetJsonDocumentAsync(
        string relativeOrAbsoluteUrl,
        CancellationToken cancellationToken = default,
        AkeneoApiCredentials apiCredentials = null
       )
    {
        using var response = await SendAuthenticatedGetAsync(
            relativeOrAbsoluteUrl, apiCredentials, cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private async Task<JsonElement?> GetJsonOrNullAsync(
        string relativeUrl,
        CancellationToken cancellationToken = default,
        AkeneoApiCredentials apiCredentials = null)
    {
        using var response = await SendAuthenticatedGetAsync(
            relativeUrl, apiCredentials, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        await EnsureSuccessAsync(response, cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return document.RootElement.Clone();
    }

    /// <summary>
    /// Performs an authenticated GET, retrying once with a forced token refresh on a 401.
    /// The caller owns the returned response and must dispose it.
    /// </summary>
    private async Task<HttpResponseMessage> SendAuthenticatedGetAsync(
        string relativeOrAbsoluteUrl,
        AkeneoApiCredentials apiCredentials,
        CancellationToken cancellationToken)
    {
        var accessToken = await EnsureAuthenticatedAsync(apiCredentials, cancellationToken);
        var response = await SendGetAsync(relativeOrAbsoluteUrl, accessToken, cancellationToken);

        if (response.StatusCode != HttpStatusCode.Unauthorized)
            return response;

        response.Dispose();
        accessToken = await AuthenticateAsync(forceRefresh: true, cancellationToken, apiCredentials);
        return await SendGetAsync(relativeOrAbsoluteUrl, accessToken, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendGetAsync(
        string relativeOrAbsoluteUrl,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(relativeOrAbsoluteUrl));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        return await _httpClient.SendAsync(request, cancellationToken);
    }

    #endregion

    #region Authentication

    private async Task<string> EnsureAuthenticatedAsync(
        AkeneoApiCredentials apiCredentials = null,
        CancellationToken cancellationToken = default)
    {
        apiCredentials ??= GetApiCredentialsFromSettings();
        ValidateCredentials(apiCredentials);
        Configure(apiCredentials.BaseUrl);

        var cachedToken = await GetCachedTokenAsync(apiCredentials);
        if (IsUsableToken(cachedToken))
            return cachedToken.AccessToken;

        return await AuthenticateAsync(forceRefresh: false, cancellationToken, apiCredentials);
    }

    private async Task<string> AuthenticateAsync(
        bool forceRefresh,
        CancellationToken cancellationToken = default,
        AkeneoApiCredentials apiCredentials = null,
        bool preventRefreshToken = false)
    {
        await _authLock.WaitAsync(cancellationToken);
        try
        {
            apiCredentials ??= GetApiCredentialsFromSettings();
            ValidateCredentials(apiCredentials);
            Configure(apiCredentials.BaseUrl);

            var cachedToken = await GetCachedTokenAsync(apiCredentials);

            if (!forceRefresh && IsUsableToken(cachedToken))
                return cachedToken.AccessToken;

            AkeneoTokenResponse tokenResponse = null;

            if (!string.IsNullOrWhiteSpace(cachedToken?.RefreshToken) && !preventRefreshToken)
            {
                tokenResponse = await TryRequestTokenAsync(
                    grantType: "refresh_token",
                    refreshToken: cachedToken.RefreshToken,
                    apiCredentials, cancellationToken);

                if (!tokenResponse.Success)
                {
                    await _logger.WarningAsync(
                        $"Akeneo refresh token grant failed. Falling back to password grant. Error: {tokenResponse.ErrorMessage}");

                    await _staticCacheManager.RemoveAsync(CreateTokenCacheKey(apiCredentials));
                    tokenResponse = null;
                }
            }

            tokenResponse ??= await TryRequestTokenAsync(
                grantType: "password",
                refreshToken: null,
                apiCredentials, cancellationToken);

            if (string.IsNullOrWhiteSpace(tokenResponse?.AccessToken))
                throw new AkeneoApiException(
                    tokenResponse?.ErrorMessage ?? "Akeneo authentication succeeded but no access token was returned.");

            var expiresIn = tokenResponse.ExpiresIn > 0
                ? tokenResponse.ExpiresIn
                : DefaultTokenLifetimeSeconds;

            var tokenCacheItem = new AkeneoTokenCacheItem
            {
                AccessToken = tokenResponse.AccessToken,
                RefreshToken = tokenResponse.RefreshToken ?? cachedToken?.RefreshToken ?? string.Empty,
                AccessTokenExpiresOnUtc = DateTime.UtcNow.AddSeconds(expiresIn)
            };

            await _staticCacheManager.SetAsync(CreateTokenCacheKey(apiCredentials), tokenCacheItem);

            return tokenCacheItem.AccessToken;
        }
        finally
        {
            _authLock.Release();
        }
    }

    private async Task<AkeneoTokenResponse> TryRequestTokenAsync(
        string grantType,
        string refreshToken,
        AkeneoApiCredentials apiCredentials,
        CancellationToken cancellationToken)
    {
        var payload = grantType == "refresh_token"
            ? JsonSerializer.Serialize(new
            {
                grant_type = "refresh_token",
                refresh_token = refreshToken
            })
            : JsonSerializer.Serialize(new
            {
                grant_type = "password",
                username = apiCredentials.Username,
                password = apiCredentials.Password
            });

        var basicToken = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{apiCredentials.ClientId}:{apiCredentials.ClientSecret}"));

        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri("api/oauth/v1/token"))
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var message = $"Akeneo token request failed. GrantType: {grantType}. Status: {response.StatusCode}. Body: {body}";

            await _logger.WarningAsync(message);
            return new AkeneoTokenResponse { ErrorMessage = message };
        }

        try
        {
            var tokenResponse = JsonSerializer.Deserialize<AkeneoTokenResponse>(
                body,
                SnakeCaseJsonOptions);

            if (tokenResponse == null)
            {
                return new AkeneoTokenResponse
                {
                    ErrorMessage = "Akeneo token response was empty or could not be deserialized."
                };
            }

            return tokenResponse;
        }
        catch (JsonException ex)
        {
            await _logger.ErrorAsync(
                $"Failed to deserialize Akeneo token response. GrantType: {grantType}. Body: {body}. Exception: {ex}");

            return new AkeneoTokenResponse
            {
                ErrorMessage = $"Failed to deserialize Akeneo token response: {ex.Message}"
            };
        }
    }

    private async Task<AkeneoTokenCacheItem> GetCachedTokenAsync(AkeneoApiCredentials apiCredentials)
        => await _staticCacheManager.GetAsync<AkeneoTokenCacheItem>(
            CreateTokenCacheKey(apiCredentials), default(AkeneoTokenCacheItem));

    private static bool IsUsableToken(AkeneoTokenCacheItem token)
        => token != null &&
           !string.IsNullOrWhiteSpace(token.AccessToken) &&
           token.AccessTokenExpiresOnUtc > DateTime.UtcNow.AddMinutes(TokenExpirationBufferMinutes);

    #endregion

    #region Helpers

    private static string ExtractQueryStringValue(
        string relativeOrAbsoluteUrl,
        string key)
    {
        if (string.IsNullOrWhiteSpace(relativeOrAbsoluteUrl) ||
            string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        string queryString = null;

        if (Uri.TryCreate(relativeOrAbsoluteUrl, UriKind.Absolute, out var absoluteUri))
        {
            queryString = absoluteUri.Query;
        }
        else
        {
            var questionMarkIndex = relativeOrAbsoluteUrl.IndexOf('?');

            if (questionMarkIndex >= 0 &&
                questionMarkIndex < relativeOrAbsoluteUrl.Length - 1)
            {
                queryString = relativeOrAbsoluteUrl[(questionMarkIndex + 1)..];
            }
        }

        if (string.IsNullOrWhiteSpace(queryString))
            return null;

        queryString = queryString.TrimStart('?');

        var parameters = queryString.Split(
            '&',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var parameter in parameters)
        {
            var separatorIndex = parameter.IndexOf('=');

            if (separatorIndex <= 0)
                continue;

            var parameterKey = Uri.UnescapeDataString(parameter[..separatorIndex]);

            if (!string.Equals(parameterKey, key, StringComparison.OrdinalIgnoreCase))
                continue;

            var parameterValue = parameter[(separatorIndex + 1)..];

            return string.IsNullOrWhiteSpace(parameterValue)
                ? null
                : Uri.UnescapeDataString(parameterValue);
        }

        return null;
    }
    private CacheKey CreateTokenCacheKey(AkeneoApiCredentials apiCredentials)
    {
        apiCredentials ??= GetApiCredentialsFromSettings();

        var cacheIdentity = string.Join("|",
            NormalizeBaseUrl(apiCredentials.BaseUrl),
            apiCredentials.ClientId,
            apiCredentials.Username);

        return new CacheKey($"Apt.Nop.Plugin.Misc.AkeneoConnection.Token.{CreateSha256Hash(cacheIdentity)}")
        {
            CacheTime = TokenCacheTimeMinutes
        };
    }

    private AkeneoApiCredentials GetApiCredentialsFromSettings(int? storeId = null)
        => new()
        {
            BaseUrl = _settings.AkeneoConnectionBaseUrl,
            ClientId = _settings.AkeneoConnectionClientId,
            ClientSecret = _settings.AkeneoConnectionClientSecret,
            Username = _settings.AkeneoConnectionUsername,
            Password = _settings.AkeneoConnectionPassword
        };

    private static void ValidateCredentials(AkeneoApiCredentials credentials)
    {
        if (credentials == null)
            throw new ArgumentNullException(nameof(credentials));

        if (string.IsNullOrWhiteSpace(credentials.BaseUrl))
            throw new AkeneoApiException("Akeneo base URL is required.");
        if (string.IsNullOrWhiteSpace(credentials.ClientId))
            throw new AkeneoApiException("Akeneo client ID is required.");
        if (string.IsNullOrWhiteSpace(credentials.ClientSecret))
            throw new AkeneoApiException("Akeneo client secret is required.");
        if (string.IsNullOrWhiteSpace(credentials.Username))
            throw new AkeneoApiException("Akeneo username is required.");
        if (string.IsNullOrWhiteSpace(credentials.Password))
            throw new AkeneoApiException("Akeneo password is required.");
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new AkeneoApiException(
            $"Akeneo API request failed. Status: {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}");
    }

    private static string GetNextPageUrl(JsonElement root)
    {
        if (root.TryGetProperty("_links", out var links) &&
            links.TryGetProperty("next", out var next) &&
            next.TryGetProperty("href", out var href))
        {
            var value = href.GetString();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        return null;
    }

    private Uri BuildUri(string relativeOrAbsoluteUrl)
    {
        if (string.IsNullOrWhiteSpace(relativeOrAbsoluteUrl))
            throw new ArgumentException("URL is required.", nameof(relativeOrAbsoluteUrl));

        // Akeneo's paging "next" links come back absolute - use them verbatim.
        if (Uri.TryCreate(relativeOrAbsoluteUrl, UriKind.Absolute, out var absolute))
            return absolute;

        if (string.IsNullOrWhiteSpace(_baseUrl))
            throw new InvalidOperationException("Akeneo base URL has not been configured.");

        return new Uri(new Uri(_baseUrl), relativeOrAbsoluteUrl.TrimStart('/'));
    }

    private static string ToQueryString(Dictionary<string, string?> query)
    {
        var parts = query
            .Where(x => !string.IsNullOrWhiteSpace(x.Value))
            .Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value!)}");

        var queryString = string.Join("&", parts);
        return queryString.Length == 0 ? string.Empty : "?" + queryString;
    }

    private static string BuildUpdatedSinceSearchJson(DateTime updatedSinceUtc)
    {
        var formattedDate = updatedSinceUtc.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss");

        return JsonSerializer.Serialize(new
        {
            updated = new[]
            {
                new { @operator = ">", value = formattedDate }
            }
        });
    }

    private async Task<T> GetObjectOrNullAsync<T>(
        string relativeUrl,
        CancellationToken cancellationToken = default,
        AkeneoApiCredentials apiCredentials = null)
    {
        using var response = await SendAuthenticatedGetAsync(
            relativeUrl,
            apiCredentials,
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return default;

        await EnsureSuccessAsync(response, cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        return JsonSerializer.Deserialize<T>(
            body,
            SnakeCaseJsonOptions);
    }

    private static string NormalizeBaseUrl(string baseUrl)
        => (baseUrl ?? string.Empty).Trim().TrimEnd('/').ToLowerInvariant();

    private static string CreateSha256Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    #endregion
}