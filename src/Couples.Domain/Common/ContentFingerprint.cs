using System.Security.Cryptography;
using System.Text;

namespace Couples.Domain.Common;

/// <summary>
/// SHA-256 of a piece of text. Used to prove that what a user approved in a preview is exactly
/// what gets shared: if the text changes after the preview, the fingerprint no longer matches.
/// </summary>
public static class ContentFingerprint
{
    public static string Of(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}
