using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Nop.Services.Configuration;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

/// <summary>
/// Produces a diagnostic configuration export. The document is intentionally
/// dictionary/object based rather than a large DTO graph, so newly added
/// properties on existing configuration entities are included automatically.
/// </summary>
public sealed class AkeneoConfigurationExportService(
    ISettingService settingService,
    IAkeneoSyncProfileService syncProfileService,
    IAkeneoFamilyMappingService familyMappingService,
    IAkeneoAttributeMappingService attributeMappingService,
    IAkeneoAssetMappingService assetMappingService,
    IAkeneoNopEntityMappingService entityMappingService)
    : IAkeneoConfigurationExportService
{
    private const string RedactedValue = "[REDACTED]";

    private static readonly string[] SensitivePropertyNameMarkers =
    {
        "password",
        "secret",
        "token",
        "apikey",
        "api_key",
        "accesskey",
        "privatekey"
    };

    private static readonly JsonSerializerOptions SerializerOptions =
        CreateSerializerOptions();

    public async Task<byte[]> ExportAsync(
        int activeStoreScopeId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var settings = await settingService.LoadSettingAsync<AkeneoConnectionSettings>(
            activeStoreScopeId);

        var syncProfilesTask = syncProfileService.GetAllAkeneoSyncProfilesAsync();
        var familyMappingsTask = familyMappingService.GetAllAsync();
        var attributeMappingsTask =
            attributeMappingService.GetAllAkeneoAttributeMappingsAsync();
        var fallbackSourcesTask =
            attributeMappingService.GetAllFallbackSourcesAsync();
        var assetMappingsTask = assetMappingService.GetAllAsync();
        var categoryMappingsTask =
            entityMappingService.GetAkeneoNopEntityMappingsAsync(
                AkeneoEntityType.Category);

        var syncProfiles = (await syncProfilesTask)
            .OrderBy(profile => profile.Id)
            .ToList();

        var familyMappings = (await familyMappingsTask)
            .OrderBy(mapping => mapping.DisplayOrder)
            .ThenBy(mapping => mapping.AkeneoFamilyCode)
            .ThenBy(mapping => mapping.Id)
            .ToList();

        var attributeMappings = (await attributeMappingsTask)
            .OrderBy(mapping => mapping.AkeneoFamilyCode)
            .ThenBy(mapping => mapping.ValueModeId)
            .ThenBy(mapping => mapping.MappingKey)
            .ThenBy(mapping => mapping.AkeneoAttributeCode)
            .ThenBy(mapping => mapping.Id)
            .ToList();

        var fallbackSources = (await fallbackSourcesTask)
            .OrderBy(source => source.AttributeMappingId)
            .ThenBy(source => source.DisplayOrder)
            .ThenBy(source => source.Id)
            .ToList();

        var fallbackSourcesByMappingId = fallbackSources
            .GroupBy(source => source.AttributeMappingId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<AkeneoAttributeMappingFallbackSource>)
                    group.ToList());

        var assetMappings = (await assetMappingsTask)
            .OrderBy(mapping => mapping.AkeneoFamilyCode)
            .ThenBy(mapping => mapping.DisplayOrder)
            .ThenBy(mapping => mapping.MappingKey)
            .ThenBy(mapping => mapping.Id)
            .ToList();

        var categoryMappings = (await categoryMappingsTask)
            .Where(mapping =>
                mapping.NopEntityTypeId == (int)NopEntityType.Category)
            .OrderBy(mapping => mapping.AkeneoCode)
            .ThenBy(mapping => mapping.Id)
            .ToList();

        var document = new SortedDictionary<string, object?>
        {
            ["metadata"] = BuildMetadata(activeStoreScopeId),
            ["connectionSettings"] = ToRedactedPropertyDictionary(settings),
            ["storeOverrides"] = await BuildStoreOverrideDictionaryAsync(
                settings,
                activeStoreScopeId),
            ["syncProfiles"] = syncProfiles,
            ["familyMappings"] = await BuildFamilyMappingSectionsAsync(
                familyMappings,
                cancellationToken),
            ["attributeMappings"] = BuildAttributeMappingSections(
                attributeMappings,
                fallbackSourcesByMappingId),
            ["assetMappings"] = assetMappings,
            ["categoryMappings"] = categoryMappings,
            ["enumCatalog"] = BuildEnumCatalog()
        };

        return JsonSerializer.SerializeToUtf8Bytes(document, SerializerOptions);
    }

    private static SortedDictionary<string, object?> BuildMetadata(
        int activeStoreScopeId)
    {
        var assembly = typeof(AkeneoConfigurationExportService).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        return new SortedDictionary<string, object?>
        {
            ["format"] = "akeneo-connection-diagnostic-configuration",
            ["formatVersion"] = 1,
            ["exportedOnUtc"] = DateTime.UtcNow,
            ["pluginSystemName"] = AkeneoConnectionConstants.SystemName,
            ["assemblyVersion"] = assembly.GetName().Version?.ToString(),
            ["assemblyInformationalVersion"] = informationalVersion,
            ["activeStoreScopeId"] = activeStoreScopeId,
            ["secretsRedacted"] = true,
            ["redactedProperties"] =
                typeof(AkeneoConnectionSettings)
                    .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                    .Where(property => IsSensitivePropertyName(property.Name))
                    .Select(property => property.Name)
                    .OrderBy(name => name)
                    .ToArray(),
            ["purpose"] =
                "Diagnostic snapshot for reviewing expected synchronization behavior. " +
                "This is not an import or backup format.",
            ["excludedOperationalData"] = new[]
            {
                "sync run records and item logs",
                "per-product synchronization state",
                "managed relation and managed asset ownership records",
                "sync leases",
                "raw Akeneo payload snapshots"
            }
        };
    }

    private static SortedDictionary<string, object?>
        ToRedactedPropertyDictionary(object source)
    {
        var result = new SortedDictionary<string, object?>();

        foreach (var property in source.GetType()
                     .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                     .Where(property =>
                         property.CanRead &&
                         property.GetIndexParameters().Length == 0)
                     .OrderBy(property => property.Name))
        {
            var value = property.GetValue(source);

            if (IsSensitivePropertyName(property.Name))
            {
                result[property.Name] = IsConfiguredSecret(value)
                    ? RedactedValue
                    : null;
                continue;
            }

            result[property.Name] = value;
        }

        return result;
    }

    private async Task<SortedDictionary<string, bool>>
        BuildStoreOverrideDictionaryAsync(
            AkeneoConnectionSettings settings,
            int activeStoreScopeId)
    {
        var result = new SortedDictionary<string, bool>();

        if (activeStoreScopeId <= 0)
        {
            result[nameof(settings.AkeneoConnectionBaseUrl)] = false;
            result[nameof(settings.AkeneoConnectionClientId)] = false;
            result[nameof(settings.AkeneoConnectionClientSecret)] = false;
            result[nameof(settings.AkeneoConnectionUsername)] = false;
            result[nameof(settings.AkeneoConnectionPassword)] = false;
            result[nameof(settings.DefaultSyncProfileId)] = false;
            return result;
        }

        result[nameof(settings.AkeneoConnectionBaseUrl)] =
            await settingService.SettingExistsAsync(
                settings,
                value => value.AkeneoConnectionBaseUrl,
                activeStoreScopeId);

        result[nameof(settings.AkeneoConnectionClientId)] =
            await settingService.SettingExistsAsync(
                settings,
                value => value.AkeneoConnectionClientId,
                activeStoreScopeId);

        result[nameof(settings.AkeneoConnectionClientSecret)] =
            await settingService.SettingExistsAsync(
                settings,
                value => value.AkeneoConnectionClientSecret,
                activeStoreScopeId);

        result[nameof(settings.AkeneoConnectionUsername)] =
            await settingService.SettingExistsAsync(
                settings,
                value => value.AkeneoConnectionUsername,
                activeStoreScopeId);

        result[nameof(settings.AkeneoConnectionPassword)] =
            await settingService.SettingExistsAsync(
                settings,
                value => value.AkeneoConnectionPassword,
                activeStoreScopeId);

        result[nameof(settings.DefaultSyncProfileId)] =
            await settingService.SettingExistsAsync(
                settings,
                value => value.DefaultSyncProfileId,
                activeStoreScopeId);

        return result;
    }

    private async Task<IReadOnlyList<object>> BuildFamilyMappingSectionsAsync(
        IReadOnlyCollection<AkeneoFamilyMapping> familyMappings,
        CancellationToken cancellationToken)
    {
        var sections = new List<object>(familyMappings.Count);

        foreach (var familyMapping in familyMappings)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var axisMappings = (await familyMappingService
                    .GetAxisMappingsAsync(familyMapping.Id))
                .OrderBy(mapping => mapping.AkeneoVariantAxisLevel)
                .ThenBy(mapping => mapping.DisplayOrder)
                .ThenBy(mapping => mapping.Id)
                .ToList();

            var subModelRules = (await familyMappingService
                    .GetSubModelRulesAsync(familyMapping.Id))
                .OrderBy(rule => rule.DisplayOrder)
                .ThenBy(rule => rule.Id)
                .ToList();

            sections.Add(new SortedDictionary<string, object?>
            {
                ["configuration"] = familyMapping,
                ["axisMappings"] = axisMappings,
                ["subModelRules"] = subModelRules
            });
        }

        return sections;
    }

    private static IReadOnlyList<object> BuildAttributeMappingSections(
        IReadOnlyCollection<AkeneoAttributeMapping> mappings,
        IReadOnlyDictionary<
            int,
            IReadOnlyList<AkeneoAttributeMappingFallbackSource>>
            fallbackSourcesByMappingId)
    {
        return mappings
            .Select(mapping => (object)new SortedDictionary<string, object?>
            {
                ["configuration"] = mapping,
                ["fallbackSources"] =
                    fallbackSourcesByMappingId.TryGetValue(
                        mapping.Id,
                        out var sources)
                        ? sources
                        : Array.Empty<AkeneoAttributeMappingFallbackSource>()
            })
            .ToList();
    }

    private static SortedDictionary<string, object> BuildEnumCatalog()
    {
        const string pluginNamespace =
            "Apt.Nop.Plugin.Misc.AkeneoConnection";

        var enumTypes = typeof(AkeneoConfigurationExportService).Assembly
            .GetTypes()
            .Where(type =>
                type.IsEnum &&
                type.Namespace?.StartsWith(
                    pluginNamespace,
                    StringComparison.Ordinal) == true)
            .OrderBy(type => type.FullName)
            .ToList();

        var catalog = new SortedDictionary<string, object>();

        foreach (var enumType in enumTypes)
        {
            var values = new SortedDictionary<string, string>(
                StringComparer.Ordinal);

            foreach (var value in Enum.GetValues(enumType))
            {
                var numericValue = Convert.ToInt64(value).ToString();
                values[numericValue] =
                    Enum.GetName(enumType, value) ?? value.ToString();
            }

            catalog[enumType.Name] = values;
        }

        return catalog;
    }

    private static bool IsSensitivePropertyName(string propertyName)
    {
        var normalizedName = propertyName
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal);

        return SensitivePropertyNameMarkers.Any(marker =>
            normalizedName.Contains(
                marker.Replace("_", string.Empty, StringComparison.Ordinal),
                StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsConfiguredSecret(object? value) =>
        value is not null &&
        !string.IsNullOrWhiteSpace(Convert.ToString(value));

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            ReferenceHandler = ReferenceHandler.IgnoreCycles
        };

        options.Converters.Add(new JsonStringEnumConverter());

        return options;
    }
}
