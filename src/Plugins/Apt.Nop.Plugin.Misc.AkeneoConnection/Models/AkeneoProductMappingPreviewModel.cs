using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Models;

public enum AkeneoDryRunOperationType
{
    NoChange = 0,
    Create = 10,
    Add = 20,
    Update = 30,
    Clear = 40,
    Remove = 50,
    Preserve = 60,
    Skip = 70,
    Review = 80
}

public record AkeneoProductMappingPreviewModel
{
    public string AkeneoProductUuid { get; set; }

    public string AkeneoProductModelCode { get; set; }

    public int AkeneoEntityTypeId { get; set; } = (int)AkeneoEntityType.Product;

    public bool IsProductModel =>
        AkeneoEntityTypeId == (int)AkeneoEntityType.ProductModel;

    public string AkeneoEntityTypeName =>
        IsProductModel ? "Product model" : "Product";

    public int? SyncProfileId { get; set; }

    public string SyncProfileName { get; set; }

    public string Locale { get; set; } = "en_US";

    public string Channel { get; set; } = "ecommerce";

    public string Currency { get; set; } = "USD";

    public bool CanImport =>
        HasSearched &&
        AkeneoProductFound &&
        ImportAllowedByProfile &&
        (IsProductModel
            ? !string.IsNullOrWhiteSpace(AkeneoProductModelCode)
            : !string.IsNullOrWhiteSpace(AkeneoProductUuid)) &&
        Errors?.Any() != true;

    public bool HasSearched { get; set; }

    public string AkeneoIdentifier { get; set; }

    public bool AkeneoProductFound { get; set; }

    public bool NopProductFound { get; set; }

    public int? NopProductId { get; set; }

    public bool ImportAllowedByProfile { get; set; } = true;

    public string ImportBlockReason { get; set; }

    public string Action { get; set; }

    /// <summary>
    /// Human-readable import path resolved for product-model and variant
    /// orchestration. This is populated before the section planners run so the
    /// Dry Run can surface parent-side effects that occur above the product
    /// synchronizer pipeline.
    /// </summary>
    public string HierarchyDecision { get; set; }

    public string ParentProductDecision { get; set; }

    public string ImmediateParentProductModelCode { get; set; }

    public string EffectiveParentProductModelCode { get; set; }

    public int? ParentNopProductId { get; set; }

    public string CurrentRepresentation { get; set; }

    public bool HierarchyRequiresReview { get; set; }

    /// <summary>
    /// A destination-oriented, read-only plan. Keeping one generic operation
    /// model makes it easy to add future synchronization areas without adding
    /// another purpose-built dry-run DTO and table.
    /// </summary>
    public IList<AkeneoDryRunOperationPreviewModel> Operations { get; set; }
        = new List<AkeneoDryRunOperationPreviewModel>();

    public IList<string> Messages { get; set; } = new List<string>();

    public IList<string> CoverageNotes { get; set; } = new List<string>();

    public IList<string> Warnings { get; set; } = new List<string>();

    public IList<string> Errors { get; set; } = new List<string>();

    public int PlannedChangeCount => Operations.Count(operation => operation.WillChange);

    public int NoChangeCount => Operations.Count(operation =>
        operation.ChangeType == AkeneoDryRunOperationType.NoChange);

    public int PreservedCount => Operations.Count(operation =>
        operation.ChangeType is AkeneoDryRunOperationType.Preserve or
            AkeneoDryRunOperationType.Skip);

    public int ReviewCount => Operations.Count(operation =>
        operation.ChangeType == AkeneoDryRunOperationType.Review);

    public bool HasPlannedChanges => PlannedChangeCount > 0;

    public bool HasIncompleteCoverage => CoverageNotes.Any();
}

public sealed class AkeneoDryRunOperationPreviewModel
{
    public int DisplayOrder { get; set; }

    public string Area { get; set; }

    public string Target { get; set; }

    public string AkeneoSource { get; set; }

    public string CurrentValue { get; set; }

    public string ProposedValue { get; set; }

    public AkeneoDryRunOperationType ChangeType { get; set; }

    public string Detail { get; set; }

    public bool IsRequired { get; set; }

    public bool WillChange =>
        ChangeType is AkeneoDryRunOperationType.Create or
            AkeneoDryRunOperationType.Add or
            AkeneoDryRunOperationType.Update or
            AkeneoDryRunOperationType.Clear or
            AkeneoDryRunOperationType.Remove;

    public string ChangeTypeName => ChangeType switch
    {
        AkeneoDryRunOperationType.NoChange => "No change",
        AkeneoDryRunOperationType.Create => "Create",
        AkeneoDryRunOperationType.Add => "Add",
        AkeneoDryRunOperationType.Update => "Update",
        AkeneoDryRunOperationType.Clear => "Clear",
        AkeneoDryRunOperationType.Remove => "Remove",
        AkeneoDryRunOperationType.Preserve => "Preserve",
        AkeneoDryRunOperationType.Skip => "Skip",
        AkeneoDryRunOperationType.Review => "Review",
        _ => ChangeType.ToString()
    };

    public string BadgeClass => ChangeType switch
    {
        AkeneoDryRunOperationType.NoChange => "badge-secondary",
        AkeneoDryRunOperationType.Create => "badge-success",
        AkeneoDryRunOperationType.Add => "badge-success",
        AkeneoDryRunOperationType.Update => "badge-primary",
        AkeneoDryRunOperationType.Clear => "badge-warning",
        AkeneoDryRunOperationType.Remove => "badge-danger",
        AkeneoDryRunOperationType.Preserve => "badge-info",
        AkeneoDryRunOperationType.Skip => "badge-secondary",
        AkeneoDryRunOperationType.Review => "badge-warning",
        _ => "badge-secondary"
    };
}
