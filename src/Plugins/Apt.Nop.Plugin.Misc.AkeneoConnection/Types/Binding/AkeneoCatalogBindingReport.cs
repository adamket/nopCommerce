namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Binding;

public enum AkeneoBindingStatus
{
    Matched = 10,
    AlreadyBound = 20,
    Unmatched = 30,
    Conflict = 40,
    Issue = 50,

    /// <summary>Created in nopCommerce by this commit and bound (categories only).</summary>
    Created = 60
}

/// <summary>
/// Result of a catalog binding scan or commit. Scans never write; a commit
/// carries the same report plus what was written.
/// </summary>
public sealed class AkeneoCatalogBindingReport
{
    public bool Success { get; set; } = true;

    public bool Committed { get; set; }

    public bool AlreadyRunning { get; set; }

    public string Message { get; set; }

    public int? SyncRunRecordId { get; set; }

    public IList<string> FamilyCodes { get; set; } = new List<string>();

    public AkeneoBindingCounts Models { get; set; } = new();

    public AkeneoBindingCounts Variants { get; set; } = new();

    public AkeneoBindingCounts AxisValues { get; set; } = new();

    public AkeneoBindingCounts Categories { get; set; } = new();

    /// <summary>
    /// Akeneo categories with no nopCommerce category: the ones "Create missing categories"
    /// would create (no <see cref="AkeneoBindingCategoryCreation.Reason"/>), then the ones it
    /// can't, with the reason. After a commit that created them, the ones it created.
    /// </summary>
    public IList<AkeneoBindingCategoryCreation> CategoryCreations { get; set; } =
        new List<AkeneoBindingCategoryCreation>();

    /// <summary>How many categories a commit with "Create missing categories" would create.</summary>
    public int CreatableCategoryCount { get; set; }

    /// <summary>Set by a commit: nopCommerce categories it created.</summary>
    public int CreatedCategoryCount { get; set; }

    public IList<AkeneoBindingConflict> Conflicts { get; set; } =
        new List<AkeneoBindingConflict>();

    public IList<AkeneoBindingIssue> Issues { get; set; } =
        new List<AkeneoBindingIssue>();

    public int IssueTotal { get; set; }

    public IList<AkeneoBindingNopProduct> UnboundProducts { get; set; } =
        new List<AkeneoBindingNopProduct>();

    public int UnboundProductTotal { get; set; }

    /// <summary>
    /// Set by a commit: conflicts resolved by choice, with the candidate
    /// parents that were not chosen. Reported only; nothing is changed on them.
    /// </summary>
    public IList<AkeneoBindingConflictResolution> Resolutions { get; set; } =
        new List<AkeneoBindingConflictResolution>();

    public int WrittenCount { get; set; }
}

public sealed class AkeneoBindingCounts
{
    public int Matched { get; set; }

    public int AlreadyBound { get; set; }

    public int Unmatched { get; set; }

    public int Conflict { get; set; }

    public int Issue { get; set; }

    public int Created { get; set; }
}

public sealed class AkeneoBindingCategoryCreation
{
    public string AkeneoCode { get; set; }

    /// <summary>The Akeneo label the nopCommerce category is named after.</summary>
    public string Name { get; set; }

    /// <summary>The Akeneo parent code; null for a top-level category.</summary>
    public string ParentCode { get; set; }

    /// <summary>Set by a commit: the nopCommerce category created.</summary>
    public int? NopCategoryId { get; set; }

    /// <summary>Why it can't be created; null when it can.</summary>
    public string Reason { get; set; }
}

public sealed class AkeneoBindingConflict
{
    public string ProductModelCode { get; set; }

    public string FamilyCode { get; set; }

    public int VariantCount { get; set; }

    public IList<AkeneoBindingCandidate> Candidates { get; set; } =
        new List<AkeneoBindingCandidate>();
}

public sealed class AkeneoBindingCandidate
{
    public int ProductId { get; set; }

    public string Name { get; set; }

    public string Sku { get; set; }

    public bool Published { get; set; }

    /// <summary>Variants whose only match points to this parent.</summary>
    public int VariantVotes { get; set; }

    /// <summary>Variants with an ambiguous SKU that could belong to this parent.</summary>
    public int AmbiguousVariants { get; set; }
}

public sealed class AkeneoBindingConflictResolution
{
    public string ProductModelCode { get; set; }

    public AkeneoBindingNopProduct Chosen { get; set; }

    public IList<AkeneoBindingNopProduct> NotChosen { get; set; } =
        new List<AkeneoBindingNopProduct>();
}

public sealed class AkeneoBindingIssue
{
    /// <summary>ProductModel, Variant, AxisValue, Family or Category.</summary>
    public string Level { get; set; }

    public string AkeneoCode { get; set; }

    public string FamilyCode { get; set; }

    public string Message { get; set; }
}

public sealed class AkeneoBindingNopProduct
{
    public int Id { get; set; }

    public string Name { get; set; }

    public string Sku { get; set; }

    public string ProductType { get; set; }

    public bool Published { get; set; }
}
