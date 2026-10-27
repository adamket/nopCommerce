using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Services.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public sealed class AkeneoCombinationAvailabilityService(
    IAkeneoProductSyncStateService syncStateService,
    IProductService productService,
    IProductAttributeService productAttributeService,
    IAkeneoManagedRelationService managedRelationService,
    IAkeneoManagedVariantCleanupService cleanupService,
    IAkeneoFamilyMappingService familyMappingService,
    IAkeneoApiClient apiClient) : IAkeneoCombinationAvailabilityService
{
    private Task<IList<AkeneoFamilyMapping>> _mappings;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Task<AkeneoProductDefinition>> _parents = new(StringComparer.OrdinalIgnoreCase);
    private Task<IList<AkeneoFamilyMapping>> GetMappingsAsync() => _mappings ??= familyMappingService.GetAllAsync();

    public static bool ManagesEnabledStatus(AkeneoFamilyMapping mapping) => mapping is { Enabled: true,
        VariantRelationshipMode: AkeneoVariantRelationshipMode.ProductAttributeCombinations }
        && mapping.ManageVariantsByEnabledStatus;

    /// <summary>What decides a managed family's combination availability, or null when unmanaged.</summary>
    public static AkeneoCombinationAvailabilitySource? GetAvailabilitySource(AkeneoFamilyMapping mapping) =>
        ManagesEnabledStatus(mapping) ? mapping.CombinationAvailabilitySource : null;

    public async Task<bool> ManagesEnabledStatusAsync(AkeneoProductDefinition source, CancellationToken cancellationToken = default) =>
        await GetAvailabilitySourceAsync(source, cancellationToken) != null;

    public async Task<AkeneoCombinationAvailabilitySource?> GetAvailabilitySourceAsync(AkeneoProductDefinition source, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(source.Parent)) return null;
        var mappings = (await GetMappingsAsync()).Where(m => string.Equals(m.AkeneoFamilyCode, source.Family, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!mappings.Any(ManagesEnabledStatus)) return null;
        // Akeneo leaves do not carry family_variant; resolve the immediate model, never guess a family default.
        var parent = await _parents.GetOrAdd(source.Parent, code => apiClient.GetProductModelByCodeAsync(code, cancellationToken));
        var resolution = Helpers.AkeneoLeafFamilyVariantResolver.Resolve(source, parent == null
            ? Array.Empty<AkeneoProductDefinition>() : new[] { parent });
        if (!resolution.Success) throw new NopException(resolution.Error);
        var mapping = mappings.FirstOrDefault(m => m.Enabled && string.Equals(m.AkeneoFamilyVariantCode?.Trim(), resolution.FamilyVariantCode, StringComparison.OrdinalIgnoreCase))
            ?? mappings.FirstOrDefault(m => m.Enabled && string.IsNullOrWhiteSpace(m.AkeneoFamilyVariantCode));
        return GetAvailabilitySource(mapping);
    }

    private async Task<IList<AkeneoFamilyMapping>> GetApplicableMappingsAsync(IEnumerable<string> familyCodes)
    {
        var codes = (familyCodes ?? Array.Empty<string>()).Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return (await GetMappingsAsync()).Where(m => codes.Count == 0 || codes.Contains(m.AkeneoFamilyCode)).ToList();
    }

    public async Task ValidateRulesAsync(IEnumerable<string> familyCodes, AkeneoProductEnabledFilter enabledFilter, bool allowUpdates)
    {
        foreach (var mapping in await GetApplicableMappingsAsync(familyCodes))
        {
            try { ValidateConfiguration(ManagesEnabledStatus(mapping), enabledFilter, allowUpdates); }
            catch (ArgumentException ex) { throw new ArgumentException($"Family mapping '{mapping.AkeneoFamilyCode}/{mapping.AkeneoFamilyVariantCode}': {ex.Message}", ex); }
        }
    }

    public async Task<string> GetConfigurationFingerprintAsync(IEnumerable<string> familyCodes) =>
        System.Text.Json.JsonSerializer.Serialize((await GetApplicableMappingsAsync(familyCodes))
            .OrderBy(m => m.AkeneoFamilyCode, StringComparer.OrdinalIgnoreCase).ThenBy(m => m.AkeneoFamilyVariantCode, StringComparer.OrdinalIgnoreCase)
            .Select(m => new { m.AkeneoFamilyCode, m.AkeneoFamilyVariantCode, m.Enabled, m.VariantRelationshipModeId, 
                // Unchanged JSON for the enabled-status source, so existing scope fingerprints keep.
                ManageVariantsByEnabledStatus = GetAvailabilitySource(m) == AkeneoCombinationAvailabilitySource.PublishedRule
                    ? (object)"published-rule" : ManagesEnabledStatus(m) }));

    public static void ValidateConfiguration(bool manageEnabledStatus, AkeneoProductEnabledFilter enabledFilter, bool allowUpdates = true)
    {
        if (!manageEnabledStatus) return;
        if (enabledFilter != AkeneoProductEnabledFilter.Any)
            throw new ArgumentException("Combination availability requires the product enabled filter to be Any, so both disabled and re-enabled SKUs are processed.");
        if (!allowUpdates)
            throw new ArgumentException("Combination availability requires a write mode that allows updates, because removing an existing combination is an update.");
    }

    public Task<bool> IsAvailableAsync(AkeneoVariantImportContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.ManageVariantsByEnabledStatus) return Task.FromResult(true);

        if (context.CombinationAvailabilitySource == AkeneoCombinationAvailabilitySource.PublishedRule)
        {
            if (context.PublishedRuleProblem != null)
                throw new NopException($"{context.PublishedRuleProblem} No combination changes were made for '{context.AkeneoIdentifier ?? context.AkeneoUuid}'.");

            // Held: an existing combination stays; a missing one is not created.
            return Task.FromResult(context.PublishedRuleHeld
                ? context.ExistingProductAttributeCombinationId.HasValue
                : context.PublishedRuleResult == true);
        }

        if (context.SourceProduct?.Enabled is not bool enabled)
            throw new NopException($"Akeneo enabled status is missing for '{context.AkeneoIdentifier ?? context.AkeneoUuid}'. No combination changes were made.");
        return Task.FromResult(enabled);
    }

    public async Task<AkeneoUnavailableCombinationPlan> GetUnavailableBindingAsync(
        AkeneoProductDefinition source, AkeneoProductImportRequest request, CancellationToken cancellationToken = default)
    {
        if (!request.SyncProfileId.HasValue || string.IsNullOrWhiteSpace(source.Parent)) return null;
        // Only enabled status is known before mappings are resolved; the Published rule is
        // decided with the variant, in AkeneoVariantRelationshipService.
        if (await GetAvailabilitySourceAsync(source, cancellationToken) != AkeneoCombinationAvailabilitySource.EnabledStatus) return null;
        const bool manageEnabledStatus = true;
        var state = await syncStateService.GetBySourceAsync(request.SyncProfileId.Value,
            AkeneoEntityType.Product, source.Identifier, source.Uuid);
        if (state == null || state.DestinationKindId != (int)AkeneoProductDestinationKind.ProductAttributeCombination)
            return null;
        var context = new AkeneoVariantImportContext
        {
            AkeneoIdentifier = source.Identifier, AkeneoUuid = source.Uuid, SourceProduct = source,
            AkeneoFamilyCode = source.Family, AkeneoFamilyVariantCode = source.FamilyVariant,
            Sku = source.Identifier, Locale = request.Locale, Channel = request.Channel, Currency = request.Currency,
            SyncProfileId = request.SyncProfileId, SyncRunRecordId = request.SyncRunRecordId,
            ExistingProductAttributeCombinationId = state.NopProductAttributeCombinationId,
            ManageVariantsByEnabledStatus = manageEnabledStatus
        };
        if (await IsAvailableAsync(context, cancellationToken)) return null;
        var parent = await productService.GetProductByIdAsync(state.NopProductId);
        var combination = state.NopProductAttributeCombinationId.HasValue
            ? await productAttributeService.GetProductAttributeCombinationByIdAsync(state.NopProductAttributeCombinationId.Value)
            : null;
        if (combination != null && combination.ProductId != state.NopProductId)
            throw new NopException("Combination availability found a binding to a different parent. No combination was removed.");
        return new(state, parent, context, combination != null);
    }

    /// <summary>
    /// Reads the variant's computed Published mapping (Variant product role) for the Published
    /// rule source: a value, held when "Apply only when" skipped it, or the problem that stops it.
    /// </summary>
    public static void ApplyPublishedRule(AkeneoVariantImportContext context, IEnumerable<Types.Sync.AkeneoResolvedMappedValue> mappedValues)
    {
        var published = (mappedValues ?? []).LastOrDefault(mapped =>
            mapped.TargetType == NopTargetType.ProductField &&
            string.Equals(mapped.Mapping?.NopTargetKey, "Published", StringComparison.OrdinalIgnoreCase));

        if (published == null)
            context.PublishedRuleProblem = "Combination availability uses the Published rule, but no Published mapping applies to variant products.";
        else if (published.IsSkipped)
            context.PublishedRuleHeld = true;
        else if (!published.HasValue)
            context.PublishedRuleProblem = "The Published rule produced no value.";
        else if (TryParseBoolean(published.DisplayValue, out var value))
            context.PublishedRuleResult = value;
        else
            context.PublishedRuleProblem = $"The Published rule produced '{published.DisplayValue}', which is not true or false.";
    }

    private static bool TryParseBoolean(string value, out bool result)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "true" or "1" or "yes": result = true; return true;
            case "false" or "0" or "no": result = false; return true;
            default: result = false; return false;
        }
    }

    public async Task<bool> RemoveAsync(Product parent, AkeneoVariantImportContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (parent.Deleted || !context.ExistingProductAttributeCombinationId.HasValue) return false;
        if (context.SyncProfileId is not > 0)
            throw new NopException("Combination removal requires a sync profile that owns the combination.");
        var id = context.ExistingProductAttributeCombinationId.Value;
        var combination = await productAttributeService.GetProductAttributeCombinationByIdAsync(id);
        if (combination == null) return false;
        if (combination.ProductId != parent.Id)
            throw new NopException("Combination availability cannot remove a combination belonging to another parent.");
        var state = await syncStateService.GetBySourceAsync(context.SyncProfileId.Value,
            AkeneoEntityType.Product, context.AkeneoIdentifier, context.AkeneoUuid);
        var tracked = state != null && state.DestinationKindId == (int)AkeneoProductDestinationKind.ProductAttributeCombination &&
            state.NopProductId == parent.Id && state.NopProductAttributeCombinationId == id;
        var managed = await managedRelationService.GetByRelationEntityAsync(context.SyncProfileId.Value,
            AkeneoManagedRelationType.ProductAttributeCombination, id);
        if (!tracked && (managed == null || managed.NopProductId != parent.Id))
            throw new NopException("Combination availability will not remove an unmanaged combination. Bind or sync it with this profile first.");

        await cleanupService.DeleteCombinationAndUnusedAxisValuesAsync(context.SyncProfileId.Value, combination, cancellationToken);
        return true;
    }
}
