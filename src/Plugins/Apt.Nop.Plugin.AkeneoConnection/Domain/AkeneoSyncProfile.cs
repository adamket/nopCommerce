using Nop.Core;

namespace Apt.Nop.Plugin.AkeneoConnection.Domain;
public class AkeneoSyncProfile : BaseEntity
{
    //defaultPublishedState? 
    public string Name { get; set; }
    public bool Enabled { get; set; }
    public string AkeneoChannel { get; set; }
    public string AkeneoLocales { get; set; }
    public string RootCategoryCode { get; set; }
    public int ImportModeId { get; set; }
    public int UnmappedAttributeBehaviorId { get; set; }

    public int DefaultWarehouseId { get; set; } //do we need this?
    public int DefaultTaxCategoryId { get; set; } //do we need this?
}

public enum AkeneoImportMode
{
    CreateAndUpdate = 10,
    CreateOnly = 20,
    UpdateOnly = 30
}

public enum UnmappedAttributeBehavior
{
    Ignore = 10,
    Log = 20,
    CreateAsSpecification = 30
}
