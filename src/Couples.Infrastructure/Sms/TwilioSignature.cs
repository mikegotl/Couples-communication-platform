using System.Security.Cryptography;
using System.Text;

namespace Couples.Infrastructure.Sms;

/// <summary>
/// Checks Twilio's <c>X-Twilio-Signature</c>: base64 HMAC-SHA1, keyed with the auth token, of the
/// full webhook URL followed by every POST parameter name and value, sorted by name.
/// </summary>
public static class TwilioSignature
{
    public static string Compute(string authToken, string url, IEnumerable<KeyValuePair<string, string>> form)
    {
        ArgumentNullException.ThrowIfNull(authToken);
        var data = new StringBuilder(url);
        foreach (var (key, value) in form.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            data.Append(key).Append(value);
        }

#pragma warning disable CA5350 // Twilio's signature scheme is defined as HMAC-SHA1.
        var hash = HMACSHA1.HashData(Encoding.UTF8.GetBytes(authToken), Encoding.UTF8.GetBytes(data.ToString()));
#pragma warning restore CA5350
        return Convert.ToBase64String(hash);
    }

    public static bool IsValid(string? authToken, string url, IEnumerable<KeyValuePair<string, string>> form, string? signature)
    {
        if (string.IsNullOrEmpty(authToken) || string.IsNullOrEmpty(signature))
        {
            return false;
        }

        var expected = Encoding.ASCII.GetBytes(Compute(authToken, url, form));
        var given = Encoding.ASCII.GetBytes(signature);
        return CryptographicOperations.FixedTimeEquals(expected, given);
    }
}
