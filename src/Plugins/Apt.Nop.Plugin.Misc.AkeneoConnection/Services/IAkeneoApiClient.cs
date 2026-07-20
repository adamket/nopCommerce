using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public interface IAkeneoApiClient
{
    Task<AkeneoProductDefinition> GetProductModelByCodeAsync(
        string code,
        CancellationToken cancellationToken = default);

    Task<AkeneoProductDefinition> GetProductByUuidAsync(
        string uuid,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AkeneoCategoryDefinition>> GetCategoriesAsync(
        int limit = 100,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AkeneoAttributeDefinition>> GetAttributesAsync(
        int limit = 100,
        CancellationToken cancellationToken = default);

    Task<AkeneoAttributeDefinition> GetAttributeByCodeAsync(
        string attributeCode,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AkeneoReferenceEntityAttributeDefinition>>
        GetReferenceEntityAttributesAsync(
            string referenceEntityCode,
            CancellationToken cancellationToken = default);

    Task<AkeneoReferenceEntityRecordDefinition> GetReferenceEntityRecordAsync(
        string referenceEntityCode,
        string recordCode,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<JsonElement>> GetAttributeOptionsAsync(
        string attributeCode,
        int limit = 100,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AkeneoFamilyDefinition>> GetFamiliesAsync(
        int limit = 100,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<JsonElement>> GetLocalesAsync(
        int limit = 100,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AkeneoProductDefinition>> GetChangedProductsAsync(
        DateTime updatedSinceUtc,
        int limit = 100,
        CancellationToken cancellationToken = default);

    Task<AkeneoConnectionTestResult> TestConnectionAsync(
        AkeneoApiCredentials apiCredentials = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AkeneoProductDefinition>> GetProductsAsync(
        string searchJson = null,
        int limit = 100,
        CancellationToken cancellationToken = default,
        AkeneoApiCredentials apiCredentials = null
       );

    Task<IReadOnlyList<AkeneoChannelDefinition>> GetChannelsAsync(
        int limit = 100,
        CancellationToken cancellationToken = default, 
        AkeneoApiCredentials apiCredentials = null);

    Task ClearCachedTokenAsync(
        AkeneoApiCredentials apiCredentials = null);

    Task<AkeneoProductPageResult> GetProductsPageAsync(
        int limit = 100,
        string searchAfter = null,
        string searchJson = null,
        CancellationToken cancellationToken = default);


    Task<IReadOnlyList<AkeneoFamilyAxis>> GetFamilyVariantAxesAsync(
        string familyCode,
        int limit = 100,
        CancellationToken cancellationToken = default);

    Task<bool> KnockAsync(
        CancellationToken cancellationToken = default);


    Task<AkeneoFamilyDefinition> GetFamilyByCodeAsync(
        string familyCode,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AkeneoAttributeOptionDefinition>>
        GetAttributeOptionDefinitionsAsync(
            string attributeCode,
            int limit = 100,
            CancellationToken cancellationToken = default);


}