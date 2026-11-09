using System.Security.Cryptography;
using System.Text;

namespace Couples.Domain.Workspaces;

/// <summary>
/// Invitation secrets. The raw token is shown to the inviter once and never stored; only its
/// SHA-256 hash is persisted, so a database or backup leak cannot be replayed as an invite.
/// </summary>
public static class InvitationToken
{
    private const int TokenBytes = 32;

    public static string Generate() =>
        Base64UrlEncode(RandomNumberGenerator.GetBytes(TokenBytes));

    public static string Hash(string rawToken)
    {
        ArgumentNullException.ThrowIfNull(rawToken);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken.Trim())));
    }

    public static bool LooksValid(string? rawToken) =>
        !string.IsNullOrWhiteSpace(rawToken) && rawToken.Trim().Length is >= 40 and <= 64;

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
