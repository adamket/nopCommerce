using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Services.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public sealed class AkeneoCombinationAvailabilityService(
    IAkeneoValueTemplateRenderer renderer,
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

    public static string GetCondition(AkeneoFamilyMapping mapping) => mapping is { Enabled: true,
        VariantRelationshipMode: AkeneoVariantRelationshipMode.ProductAttributeCombinations }
        ? mapping.CombinationAvailabilityCondition?.Trim() : null;

    public async Task<string> GetConditionAsync(AkeneoProductDefinition source, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(source.Parent)) return null;
        var mappings = (await GetMappingsAsync()).Where(m => string.Equals(m.AkeneoFamilyCode, source.Family, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!mappings.Any(m => !string.IsNullOrWhiteSpace(GetCondition(m)))) return null;
        // Akeneo leaves do not carry family_variant; resolve the immediate model, never guess a family default.
        var parent = await _parents.GetOrAdd(source.Parent, code => apiClient.GetProductModelByCodeAsync(code, cancellationToken));
        var resolution = Helpers.AkeneoLeafFamilyVariantResolver.Resolve(source, parent == null
            ? Array.Empty<AkeneoProductDefinition>() : new[] { parent });
        if (!resolution.Success) throw new NopException(resolution.Error);
        var mapping = mappings.FirstOrDefault(m => m.Enabled && string.Equals(m.AkeneoFamilyVariantCode?.Trim(), resolution.FamilyVariantCode, StringComparison.OrdinalIgnoreCase))
            ?? mappings.FirstOrDefault(m => m.Enabled && string.IsNullOrWhiteSpace(m.AkeneoFamilyVariantCode));
        return GetCondition(mapping);
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
            try { ValidateConfiguration(GetCondition(mapping), enabledFilter, allowUpdates); }
            catch (ArgumentException ex) { throw new ArgumentException($"Family mapping '{mapping.AkeneoFamilyCode}/{mapping.AkeneoFamilyVariantCode}': {ex.Message}", ex); }
        }
    }

    public async Task<string> GetConfigurationFingerprintAsync(IEnumerable<string> familyCodes) =>
        System.Text.Json.JsonSerializer.Serialize((await GetApplicableMappingsAsync(familyCodes))
            .OrderBy(m => m.AkeneoFamilyCode, StringComparer.OrdinalIgnoreCase).ThenBy(m => m.AkeneoFamilyVariantCode, StringComparer.OrdinalIgnoreCase)
            .Select(m => new { m.AkeneoFamilyCode, m.AkeneoFamilyVariantCode, m.Enabled, m.VariantRelationshipModeId, Condition = GetCondition(m) }));

    public static void ValidateConfiguration(string condition, AkeneoProductEnabledFilter enabledFilter, bool allowUpdates = true)
    {
        if (string.IsNullOrWhiteSpace(condition)) return;
        var validation = AkeneoMappingCondition.Validate(condition,
            new AkeneoValueTemplateRenderer(new AkeneoProductValueResolver()));
        if (!validation.Success)
            throw new ArgumentException("Invalid combination availability condition: " + string.Join(" ", validation.Errors));
        if (enabledFilter != AkeneoProductEnabledFilter.Any)
            throw new ArgumentException("Combination availability requires the product enabled filter to be Any, so both disabled and re-enabled SKUs are processed.");
        if (!allowUpdates)
            throw new ArgumentException("Combination availability requires a write mode that allows updates, because removing an existing combination is an update.");
    }

    public async Task<bool> IsAvailableAsync(AkeneoVariantImportContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(context.CombinationAvailabilityCondition)) return true;
        var evaluated = await AkeneoMappingCondition.EvaluateAsync(context.CombinationAvailabilityCondition, renderer,
            new AkeneoValueTemplateContext
            {
                Source = context.SourceProduct, Locale = context.Locale, Channel = context.Channel,
                // Use source metadata consistently, including before destination mappings are prepared.
                Currency = context.Currency, Sku = context.SourceProduct?.Identifier,
                FamilyCode = context.SourceProduct?.Family, FamilyVariantCode = context.SourceProduct?.FamilyVariant
            }, cancellationToken);
        if (!evaluated.Success)
            throw new NopException($"Combination availability could not be evaluated for '{context.AkeneoIdentifier ?? context.AkeneoUuid}': {evaluated.Error}. No combination changes were made.");
        return evaluated.Matches;
    }

    public async Task<AkeneoUnavailableCombinationPlan> GetUnavailableBindingAsync(
        AkeneoProductDefinition source, AkeneoProductImportRequest request, CancellationToken cancellationToken = default)
    {
        if (!request.SyncProfileId.HasValue || string.IsNullOrWhiteSpace(source.Parent)) return null;
        var condition = await GetConditionAsync(source, cancellationToken);
        if (string.IsNullOrWhiteSpace(condition)) return null;
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
            CombinationAvailabilityCondition = condition
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
