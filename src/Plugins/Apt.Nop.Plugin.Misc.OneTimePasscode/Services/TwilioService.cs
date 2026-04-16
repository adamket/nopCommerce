using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Apt.Nop.Plugin.Misc.OneTimePasscode.Services;

public class TwilioService(IHttpClientFactory httpClientFactory, OtpSettings settings)
    : ITwilioService
{
 
    private const string TWILIO_API_BASE = "https://api.twilio.com/2010-04-01";

    public async Task<(bool Success, string Response)> SendSmsAsync(string from, string to, string body)
    {
        if (string.IsNullOrWhiteSpace(from))
            throw new ArgumentException("From number must be provided.", nameof(from));
        if (string.IsNullOrWhiteSpace(to))
            throw new ArgumentException("To number must be provided.", nameof(to));
        if (body is null)
            throw new ArgumentNullException(nameof(body));


        var client = httpClientFactory.CreateClient("twilio-client");
        var url = $"{TWILIO_API_BASE}/Accounts/{settings.TwilioAccountSid}/Messages.json";

        var form = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("To", to),
            new KeyValuePair<string, string>("From", from),
            new KeyValuePair<string, string>("Body", body),
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = form;

        var authValue = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.TwilioAccountSid}:{settings.TwilioAuthToken}"));
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Basic", authValue);

        using var response = await client.SendAsync(request);
        var responseContent = await response.Content.ReadAsStringAsync();

        return (response.IsSuccessStatusCode, responseContent);
    }
}
