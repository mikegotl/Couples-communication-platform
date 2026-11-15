using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using Couples.Application.Sms;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Couples.Infrastructure.Sms;

/// <summary>
/// Sends through Twilio's Messages REST API. Credentials come from configuration
/// (<c>Sms:Twilio:*</c>, user-secrets or Key Vault). The number and the text are never logged.
/// </summary>
internal sealed partial class TwilioSmsSender(HttpClient http, IOptions<SmsOptions> options, ILogger<TwilioSmsSender> logger) : ISmsSender
{
    /// <summary>Twilio's error for a number that replied STOP.</summary>
    private const int UnsubscribedRecipient = 21610;

    public async Task<SmsSendResult> SendAsync(string toE164, string body, CancellationToken cancellationToken = default)
    {
        var twilio = options.Value.Twilio;
        if (!options.Value.IsConfigured || options.Value.Provider != "Twilio")
        {
            return new SmsSendResult(false);
        }

        var form = new List<KeyValuePair<string, string>> { new("To", toE164), new("Body", body) };
        form.Add(string.IsNullOrWhiteSpace(twilio.MessagingServiceSid)
            ? new("From", twilio.FromNumber!)
            : new("MessagingServiceSid", twilio.MessagingServiceSid));

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"https://api.twilio.com/2010-04-01/Accounts/{Uri.EscapeDataString(twilio.AccountSid!)}/Messages.json"))
        {
            Content = new FormUrlEncodedContent(form),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{twilio.AccountSid}:{twilio.AuthToken}")));

        using var response = await http.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<TwilioResponse>(cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return new SmsSendResult(true, payload?.Sid);
        }

        LogRejected(logger, (int)response.StatusCode, payload?.Code);
        return new SmsSendResult(false, null, payload?.Code == UnsubscribedRecipient);
    }

    private sealed record TwilioResponse([property: JsonPropertyName("sid")] string? Sid, [property: JsonPropertyName("code")] int? Code);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Twilio refused a text: HTTP {Status}, error {Code}")]
    private static partial void LogRejected(ILogger logger, int status, int? code);
}
