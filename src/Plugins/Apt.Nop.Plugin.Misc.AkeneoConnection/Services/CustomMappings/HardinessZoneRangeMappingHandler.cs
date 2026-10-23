using System.Globalization;
using System.Text.RegularExpressions;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services.Planning;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Nop.Core.Domain.Catalog;
using Nop.Services.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services.CustomMappings;

/// <summary>
/// Reads hardiness_zone_low and hardiness_zone_high from the product's linked
/// reference-entity record (plant_info) and assigns every whole zone from low
/// to high as an option of the mapping's specification attribute
/// (NopTargetEntityId). Options are matched by the single whole number in
/// their name ("Hardiness Zone 3" is zone 3). It never creates a
/// specification attribute or option: a zone with no option is skipped with a
/// warning. Removals follow the profile's specification sync mode.
/// </summary>
public sealed partial class HardinessZoneRangeMappingHandler(
    IAkeneoReferenceEntityValueResolver referenceEntityValueResolver,
    ISpecificationAttributeService specificationAttributeService,
    IAkeneoManagedRelationService managedRelationService)
    : IAkeneoCustomMappingHandler
{
    public const string HandlerKey = "hardiness-zone-range";
    public const string LowFieldCode = "hardiness_zone_low";
    public const string HighFieldCode = "hardiness_zone_high";

    private const string Area = "Hardiness zones";
    private const int MaximumZone = 99;

    public string Key => HandlerKey;
    public string DisplayName => "Hardiness zone range (specification)";
    public IReadOnlyCollection<string> SupportedAkeneoTypes => ["akeneo_reference_entity"];
    public bool TargetsSpecificationAttribute => true;

    public IEnumerable<string> Validate(AkeneoAttributeMapping mapping, AkeneoAttributeDefinition attribute)
    {
        if (!string.Equals(attribute?.Type, "akeneo_reference_entity", StringComparison.OrdinalIgnoreCase))
            yield return "Hardiness zones require a single reference-entity attribute such as plant_info.";

        // The handler reads both zone fields itself. Selecting the record code
        // also makes delta runs revisit products when the record changes.
        if (!string.Equals(mapping?.AkeneoReferenceEntityAttributeCode?.Trim(),
                AkeneoReferenceEntityValueResolver.RecordCodeField, StringComparison.OrdinalIgnoreCase))
            yield return "Choose \"Record code\" as the reference entity field; the handler reads hardiness_zone_low and hardiness_zone_high itself.";

        if (mapping?.NopTargetEntityId is not > 0)
            yield return "Select the specification attribute that receives the hardiness zones.";
    }

    /// <summary>
    /// Ownership is recorded under its own code so a plain specification
    /// mapping from the same Akeneo attribute can never claim these zones.
    /// </summary>
    internal static string OwnershipCode(AkeneoAttributeMapping mapping) =>
        AkeneoMappingHelper.GetSourceMappingCode(mapping) + "#" + HandlerKey;

    /// <summary>The zone an option represents: the one whole number in its name.</summary>
    internal static int? ZoneOf(SpecificationAttributeOption option)
    {
        var numbers = WholeNumber().Matches(option?.Name ?? string.Empty);
        return numbers.Count == 1 &&
               int.TryParse(numbers[0].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var zone)
            ? zone
            : null;
    }

    public async Task PlanAsync(AkeneoCustomMappingContext custom, AkeneoExecutableSectionPlan plan,
        CancellationToken cancellationToken = default)
    {
        var context = custom.ProductContext;
        var mapped = custom.MappedValue;
        var mapping = mapped.Mapping;
        var source = AkeneoMappingHelper.GetSourceDisplayName(mapping);
        var syncMode = context.Request.SpecificationAttributeSyncMode;

        if (syncMode == AkeneoCollectionSyncMode.Disabled)
        {
            plan.CoverageNotes.Add("Hardiness zones are not synced: specification-attribute synchronization is disabled by the sync profile.");
            return;
        }

        var specificationAttributeId = mapping.NopTargetEntityId ?? 0;
        var definition = specificationAttributeId > 0
            ? await specificationAttributeService.GetSpecificationAttributeByIdAsync(specificationAttributeId)
            : null;
        void Review(string target, string message) =>
            plan.AddReview(Area, target, source, message + " No hardiness zones were changed.", mapping.IsRequired);

        if (definition == null)
        {
            Review($"Specification attribute #{specificationAttributeId}",
                "The hardiness zone mapping has no valid nopCommerce specification attribute.");
            return;
        }

        if (context.GetMappings(NopTargetType.SpecificationAttribute)
            .Any(other => other.Mapping.NopTargetEntityId == specificationAttributeId))
        {
            Review(definition.Name,
                $"A specification mapping also writes '{definition.Name}', so the two would compete for its options.");
            return;
        }

        if (!mapped.HasValue)
        {
            Review(definition.Name, $"The product has no {mapping.AkeneoAttributeCode} record.");
            return;
        }

        var (low, lowError) = await ResolveZoneAsync(context, mapping, LowFieldCode, cancellationToken);
        var (high, highError) = await ResolveZoneAsync(context, mapping, HighFieldCode, cancellationToken);
        if (lowError != null || highError != null)
        {
            Review(definition.Name, lowError ?? highError);
            return;
        }

        if (low > high)
        {
            Review(definition.Name, $"{LowFieldCode} ({low}) is greater than {HighFieldCode} ({high}).");
            return;
        }

        var options = await specificationAttributeService
            .GetSpecificationAttributeOptionsBySpecificationAttributeAsync(specificationAttributeId);
        var optionsByZone = options
            .Select(option => (Option: option, Zone: ZoneOf(option)))
            .Where(item => item.Zone.HasValue)
            .GroupBy(item => item.Zone.Value)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Option).ToList());
        var optionIds = options.Select(option => option.Id).ToHashSet();

        var product = context.Product ?? context.ExistingProduct;
        var currentAssignments = product == null
            ? new List<ProductSpecificationAttribute>()
            : (await specificationAttributeService.GetProductSpecificationAttributesAsync(product.Id))
                .Where(assignment => optionIds.Contains(assignment.SpecificationAttributeOptionId))
                .ToList();
        var ownershipCode = OwnershipCode(mapping);
        var desiredOptionIds = new HashSet<int>();

        for (var zone = low; zone <= high; zone++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = $"{definition.Name}: zone {zone}";

            if (!optionsByZone.TryGetValue(zone, out var matches))
            {
                plan.Warnings.Add($"'{definition.Name}' has no option for hardiness zone {zone}, so that zone was skipped.");
                continue;
            }

            if (matches.Count > 1)
            {
                plan.Warnings.Add(
                    $"'{definition.Name}' has more than one option for hardiness zone {zone} " +
                    $"({string.Join(", ", matches.Select(option => option.Name))}), so that zone was skipped.");
                continue;
            }

            var option = matches[0];
            desiredOptionIds.Add(option.Id);

            if (currentAssignments.Any(assignment => assignment.SpecificationAttributeOptionId == option.Id))
            {
                plan.AddOperation(Area, target, source, option.Name, option.Name, AkeneoDryRunOperationType.NoChange,
                    isRequired: mapping.IsRequired);
                continue;
            }

            var zoneCode = zone.ToString(CultureInfo.InvariantCulture);
            plan.AddOperation(Area, target, source, "Not assigned", option.Name, AkeneoDryRunOperationType.Add,
                "The hardiness zone option would be assigned to the product.", mapping.IsRequired,
                async _ =>
                {
                    if (context.Product == null)
                        return false;

                    var assignment = new ProductSpecificationAttribute
                    {
                        ProductId = context.Product.Id,
                        AttributeTypeId = (int)SpecificationAttributeType.Option,
                        SpecificationAttributeOptionId = option.Id,
                        AllowFiltering = true,
                        ShowOnProductPage = true,
                        DisplayOrder = 0
                    };
                    await specificationAttributeService.InsertProductSpecificationAttributeAsync(assignment);

                    if (context.Request.SyncProfileId.HasValue)
                    {
                        await managedRelationService.UpsertAsync(context.Request.SyncProfileId.Value,
                            context.Request.SyncRunRecordId, context.Product.Id,
                            AkeneoManagedRelationType.ProductSpecificationAssignment, assignment.Id,
                            ownershipCode, zoneCode);
                    }

                    return true;
                });
        }

        await PlanRemovalsAsync(context, plan, definition, source, mapping, syncMode, product,
            currentAssignments, desiredOptionIds, ownershipCode, options);
    }

    private async Task PlanRemovalsAsync(
        AkeneoProductSyncContext context,
        AkeneoExecutableSectionPlan plan,
        SpecificationAttribute definition,
        string source,
        AkeneoAttributeMapping mapping,
        AkeneoCollectionSyncMode syncMode,
        Product product,
        IReadOnlyList<ProductSpecificationAttribute> currentAssignments,
        IReadOnlySet<int> desiredOptionIds,
        string ownershipCode,
        IList<SpecificationAttributeOption> options)
    {
        if (syncMode == AkeneoCollectionSyncMode.Merge || product == null)
            return;

        string NameOf(ProductSpecificationAttribute assignment) =>
            options.FirstOrDefault(option => option.Id == assignment.SpecificationAttributeOptionId)?.Name ??
            $"Option #{assignment.SpecificationAttributeOptionId}";

        if (syncMode == AkeneoCollectionSyncMode.ReplaceAll)
        {
            foreach (var stale in currentAssignments.Where(assignment =>
                         !desiredOptionIds.Contains(assignment.SpecificationAttributeOptionId)))
            {
                plan.AddOperation(Area, $"{definition.Name}: {NameOf(stale)}", source, NameOf(stale), "Removed",
                    AkeneoDryRunOperationType.Remove, "Replace-all makes this specification attribute match the zone range.",
                    mapping.IsRequired,
                    async _ =>
                    {
                        await specificationAttributeService.DeleteProductSpecificationAttributeAsync(stale);
                        await managedRelationService.DeleteByRelationEntityAsync(
                            AkeneoManagedRelationType.ProductSpecificationAssignment, stale.Id);
                        return true;
                    });
            }

            return;
        }

        if (!context.Request.SyncProfileId.HasValue)
        {
            plan.Warnings.Add("Replace-managed hardiness zone synchronization requires a saved sync profile. No zones were removed.");
            return;
        }

        var owned = await managedRelationService.GetByProductAsync(context.Request.SyncProfileId.Value, product.Id,
            AkeneoManagedRelationType.ProductSpecificationAssignment, ownershipCode);

        foreach (var relation in owned)
        {
            var assignment = currentAssignments.FirstOrDefault(item => item.Id == relation.NopRelationEntityId);

            if (assignment == null)
            {
                // The zone was removed in nopCommerce; drop the stale ownership row.
                plan.AddInternalOperation(async _ =>
                {
                    await managedRelationService.DeleteAsync(relation);
                    return false;
                });
                continue;
            }

            if (desiredOptionIds.Contains(assignment.SpecificationAttributeOptionId))
                continue;

            plan.AddOperation(Area, $"{definition.Name}: {NameOf(assignment)}", source, NameOf(assignment), "Removed",
                AkeneoDryRunOperationType.Remove, "This zone was written by the plugin and is now outside the range.",
                mapping.IsRequired,
                async _ =>
                {
                    await specificationAttributeService.DeleteProductSpecificationAttributeAsync(assignment);
                    await managedRelationService.DeleteAsync(relation);
                    return true;
                });
        }
    }

    /// <summary>
    /// Reads one zone field from the linked record through the shared
    /// reference-entity resolver, which caches records for the run.
    /// </summary>
    private async Task<(int Zone, string Error)> ResolveZoneAsync(
        AkeneoProductSyncContext context,
        AkeneoAttributeMapping mapping,
        string fieldCode,
        CancellationToken cancellationToken)
    {
        var fieldMapping = new AkeneoAttributeMapping
        {
            AkeneoAttributeCode = mapping.AkeneoAttributeCode,
            AkeneoReferenceEntityCode = mapping.AkeneoReferenceEntityCode,
            AkeneoReferenceEntityAttributeCode = fieldCode
        };

        var value = await referenceEntityValueResolver.ResolveAsync(
            context.Source,
            fieldMapping,
            mapping.Locale.TrimOr(context.Request.Locale),
            mapping.Channel.TrimOr(context.Request.Channel),
            context.Request.Currency,
            cancellationToken);

        IEnumerable<string> rawValues = value?.DisplayValues is { Count: > 0 } values
            ? values
            : new[] { value?.DisplayValue };
        var texts = rawValues
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => text.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (texts.Count == 0)
            return (0, $"The {mapping.AkeneoAttributeCode} record has no {fieldCode} value.");

        if (texts.Count > 1)
            return (0, $"{fieldCode} has more than one value ({string.Join(", ", texts)}).");

        if (!decimal.TryParse(texts[0], NumberStyles.Number, CultureInfo.InvariantCulture, out var number) ||
            number != decimal.Truncate(number) || number < 0 || number > MaximumZone)
        {
            return (0, $"{fieldCode} '{texts[0]}' is not a whole hardiness zone between 0 and {MaximumZone}.");
        }

        return ((int)number, null);
    }

    [GeneratedRegex(@"\b\d+\b")]
    private static partial Regex WholeNumber();
}
