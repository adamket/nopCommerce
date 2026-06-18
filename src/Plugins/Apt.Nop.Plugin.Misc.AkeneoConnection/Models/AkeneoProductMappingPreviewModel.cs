namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
public record AkeneoProductMappingPreviewModel
{
    public string AkeneoProductUuid { get; set; }

    public string Locale { get; set; } = "en_US";

    public string Channel { get; set; } = "ecommerce";

    public string Currency { get; set; } = "USD";

    public bool CanImport =>
        HasSearched &&
        AkeneoProductFound &&
        !string.IsNullOrWhiteSpace(AkeneoProductUuid) &&
        Errors?.Any() != true;

    public bool HasSearched { get; set; }

    public string AkeneoIdentifier { get; set; }

    public bool AkeneoProductFound { get; set; }

    public bool NopProductFound { get; set; }

    public int? NopProductId { get; set; }

    public string Action { get; set; }

    public IList<AkeneoMappedFieldPreviewModel> ProductFields { get; set; } = new List<AkeneoMappedFieldPreviewModel>();

    public IList<AkeneoMappedAttributePreviewModel> SpecificationAttributes { get; set; } = new List<AkeneoMappedAttributePreviewModel>();

    public IList<AkeneoMappedAttributePreviewModel> ProductAttributes { get; set; } = new List<AkeneoMappedAttributePreviewModel>();
    public IList<AkeneoMappedFieldPreviewModel> SeoFields { get; set; } =
        new List<AkeneoMappedFieldPreviewModel>();

    public IList<AkeneoMappedFieldPreviewModel> CustomProperties { get; set; } =
        new List<AkeneoMappedFieldPreviewModel>();

    public IList<string> Warnings { get; set; } = new List<string>();

    public IList<string> Errors { get; set; } = new List<string>();

    public class AkeneoMappedFieldPreviewModel
    {
        public string AkeneoAttributeCode { get; set; }

        public string TargetKey { get; set; }

        public string Value { get; set; }

        public bool IsRequired { get; set; }

        public bool HasValue => !string.IsNullOrWhiteSpace(Value);
    }

    public class AkeneoMappedAttributePreviewModel
    {
        public string AkeneoAttributeCode { get; set; }

        public int? NopAttributeId { get; set; }

        public string NopAttributeName { get; set; }

        public string Value { get; set; }

        public bool IsRequired { get; set; }

        public bool HasValue => !string.IsNullOrWhiteSpace(Value);
    }


}