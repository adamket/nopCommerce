namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Models;

public sealed record AkeneoAttributeMappingFallbackSourceModel
{
    public string AkeneoAttributeCode { get; set; }

    public int AkeneoAttributeTypeId { get; set; }

    public string AkeneoReferenceEntityCode { get; set; }

    public string AkeneoReferenceEntityAttributeCode { get; set; }

    public int DisplayOrder { get; set; }
}

public sealed record AkeneoAttributeMappingSourceOptionModel
{
    public string Key { get; set; }

    public string Text { get; set; }

    public string AkeneoAttributeCode { get; set; }

    public int AkeneoAttributeTypeId { get; set; }

    public string AkeneoReferenceEntityCode { get; set; }

    public string AkeneoReferenceEntityAttributeCode { get; set; }
}
