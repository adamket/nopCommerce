using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public interface IAkeneoApiClient
{
    Task<JsonElement?> GetProductByUuidAsync(
        string uuid,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<JsonElement>> GetCategoriesAsync(
        int limit = 100,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AkeneoAttributeDefinition>> GetAttributesAsync(
        int limit = 100,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<JsonElement>> GetAttributeOptionsAsync(
        string attributeCode,
        int limit = 100,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<JsonElement>> GetFamiliesAsync(
        int limit = 100,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<JsonElement>> GetLocalesAsync(
        int limit = 100,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<JsonElement>> GetChangedProductsAsync(
        DateTime updatedSinceUtc,
        int limit = 100,
        CancellationToken cancellationToken = default);

    Task<AkeneoConnectionTestResult> TestConnectionAsync(
        AkeneoApiCredentials apiCredentials = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<JsonElement>> GetProductsAsync(
        string searchJson = null,
        int limit = 100,
        CancellationToken cancellationToken = default,
        AkeneoApiCredentials apiCredentials = null
       );

    Task<IReadOnlyList<JsonElement>> GetChannelsAsync(
        int limit = 100,
        CancellationToken cancellationToken = default, 
        AkeneoApiCredentials apiCredentials = null);

    Task ClearCachedTokenAsync(
        AkeneoApiCredentials apiCredentials = null);

}