namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api;
public class AkeneoTokenResponse
{
    public string AccessToken { get; set; } = string.Empty;

    public int ExpiresIn { get; set; }

    public string TokenType { get; set; } = string.Empty;

    public string Scope { get; set; }

    public string RefreshToken { get; set; }

    public string ErrorMessage { get; set; }

    public bool Success => string.IsNullOrEmpty(ErrorMessage);
}
