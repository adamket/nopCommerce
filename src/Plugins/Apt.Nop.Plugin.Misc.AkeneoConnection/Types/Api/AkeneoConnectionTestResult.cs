namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api;
public class AkeneoConnectionTestResult
{
    public bool Success { get; set; }

    public string Message { get; set; } = string.Empty;

    public static AkeneoConnectionTestResult SuccessResult(string message)
    {
        return new AkeneoConnectionTestResult
        {
            Success = true,
            Message = message
        };
    }

    public static AkeneoConnectionTestResult FailureResult(string message)
    {
        return new AkeneoConnectionTestResult
        {
            Success = false,
            Message = message
        };
    }
}