using Nop.Core;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;

/// <summary>
/// What the last category content sync did for one Akeneo category, and the
/// picture it owns. A picture is only replaced in place or deleted when the
/// category still shows the picture this sync created.
/// </summary>
public class AkeneoCategoryContentState : BaseEntity
{
    public string AkeneoCategoryCode { get; set; }

    public int NopCategoryId { get; set; }

    /// <summary>Akeneo's file path of the image last written; it changes whenever the image does.</summary>
    public string PictureFilePath { get; set; }

    /// <summary>The nopCommerce picture this sync created for the category, if any.</summary>
    public int? NopPictureId { get; set; }

    public int LastStatusId { get; set; }

    public AkeneoCategoryContentStatus LastStatus
    {
        get => (AkeneoCategoryContentStatus)LastStatusId;
        set => LastStatusId = (int)value;
    }

    public string LastMessage { get; set; }

    public DateTime? LastSyncedOnUtc { get; set; }

    public DateTime UpdatedOnUtc { get; set; }
}

public enum AkeneoCategoryContentStatus
{
    Unchanged = 10,
    Updated = 20,
    Skipped = 30,
    Failed = 40
}
