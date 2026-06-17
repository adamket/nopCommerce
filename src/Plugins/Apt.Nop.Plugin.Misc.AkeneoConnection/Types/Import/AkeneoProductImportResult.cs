namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;

public class AkeneoProductImportResult
{
    public int NopProductId { get; set; }

    public string AkeneoProductUuid { get; set; }

    public string AkeneoProductKey { get; set; }

    public string AkeneoIdentifier { get; set; }

    public string Sku { get; set; }

    public bool Created { get; set; }

    public bool Updated { get; set; }

    public bool Skipped { get; set; }

    public IList<string> Messages { get; } = new List<string>();

    public IList<string> Warnings { get; } = new List<string>();

    public IList<string> Errors { get; } = new List<string>();

    public bool Success => !Errors.Any();

    public void AddMessage(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
            Messages.Add(message);
    }

    public void AddWarning(string warning)
    {
        if (!string.IsNullOrWhiteSpace(warning))
            Warnings.Add(warning);
    }

    public void AddError(string error)
    {
        if (!string.IsNullOrWhiteSpace(error))
            Errors.Add(error);
    }
}