namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api;
public class AkeneoApiException : Exception
{
    public AkeneoApiException(string message)
        : base(message)
    {
    }

    public AkeneoApiException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
