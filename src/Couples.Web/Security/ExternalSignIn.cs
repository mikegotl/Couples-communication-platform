using System.Security.Claims;
using AspNet.Security.OAuth.Apple;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Identity;

namespace Couples.Web.Security;

/// <summary>Google sign-in settings. Both values come from configuration (user secrets, Key Vault), never the repo.</summary>
public sealed class GoogleSignInOptions
{
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}

/// <summary>
/// Sign in with Apple settings. <see cref="ClientId"/> is the Services ID; the client secret is a
/// short-lived JWT the app signs with the .p8 key, given either as its text (<see cref="PrivateKey"/>,
/// suits Key Vault) or as a file path (<see cref="PrivateKeyPath"/>).
/// </summary>
public sealed class AppleSignInOptions
{
    public string? ClientId { get; set; }
    public string? TeamId { get; set; }
    public string? KeyId { get; set; }
    public string? PrivateKey { get; set; }
    public string? PrivateKeyPath { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(TeamId) && !string.IsNullOrWhiteSpace(KeyId)
        && (!string.IsNullOrWhiteSpace(PrivateKey) || !string.IsNullOrWhiteSpace(PrivateKeyPath));
}

/// <summary>
/// Optional Google and Apple sign-in alongside email and password accounts. A provider is
/// registered only when all of its settings are present, so an unconfigured provider has no
/// button, no callback endpoint and no CSP allowance.
/// </summary>
public static class ExternalSignIn
{
    public const string GoogleSection = "Authentication:Google";
    public const string AppleSection = "Authentication:Apple";

    /// <summary>Where a sign-in form post is redirected to, per provider; the CSP must allow it in form-action.</summary>
    private static readonly Dictionary<string, string> AuthorizationOrigins = new(StringComparer.Ordinal)
    {
        [GoogleDefaults.AuthenticationScheme] = "https://accounts.google.com",
        [AppleAuthenticationDefaults.AuthenticationScheme] = "https://appleid.apple.com",
    };

    public static AuthenticationBuilder AddExternalSignIn(this AuthenticationBuilder authentication, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(authentication);
        ArgumentNullException.ThrowIfNull(configuration);

        var google = configuration.GetSection(GoogleSection).Get<GoogleSignInOptions>() ?? new();
        var apple = configuration.GetSection(AppleSection).Get<AppleSignInOptions>() ?? new();
        var origins = new List<string>();

        if (google.IsConfigured)
        {
            authentication.AddGoogle(options =>
            {
                options.ClientId = google.ClientId!;
                options.ClientSecret = google.ClientSecret!;
                options.SignInScheme = IdentityConstants.ExternalScheme;
                // Only an address Google has verified may be matched to an existing account.
                // Google's userinfo v2 calls it verified_email, v3 and OpenID Connect email_verified.
                options.ClaimActions.MapJsonKey(VerifiedEmailClaim, "email_verified");
                options.ClaimActions.MapJsonKey(VerifiedEmailClaim, "verified_email");
                options.AccessDeniedPath = "/Account/Login";
            });
            origins.Add(AuthorizationOrigins[GoogleDefaults.AuthenticationScheme]);
        }

        if (apple.IsConfigured)
        {
            authentication.AddApple(options =>
            {
                options.ClientId = apple.ClientId!;
                options.TeamId = apple.TeamId!;
                options.KeyId = apple.KeyId!;
                options.GenerateClientSecret = true;
                options.SignInScheme = IdentityConstants.ExternalScheme;
                options.Scope.Add("email");
                options.AccessDeniedPath = "/Account/Login";
                var keyText = apple.PrivateKey;
                var keyPath = apple.PrivateKeyPath;
                options.PrivateKey = async (_, cancellationToken) =>
                    (!string.IsNullOrWhiteSpace(keyText) ? keyText : await File.ReadAllTextAsync(keyPath!, cancellationToken)).AsMemory();
            });
            origins.Add(AuthorizationOrigins[AppleAuthenticationDefaults.AuthenticationScheme]);
        }

        authentication.Services.AddSingleton(new ExternalSignInOrigins(origins));
        return authentication;
    }

    /// <summary>Claim carrying the provider's own "email is verified" flag, where it sends one.</summary>
    public const string VerifiedEmailClaim = "bt:email_verified";

    /// <summary>
    /// The provider's email address if the provider vouches that it belongs to the person, else
    /// null. Apple only issues verified addresses (including private relay ones); Google says so
    /// per account. Anything else is never trusted to match an existing account.
    /// </summary>
    public static string? VerifiedEmail(string loginProvider, ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        var email = principal.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var verified = loginProvider switch
        {
            AppleAuthenticationDefaults.AuthenticationScheme => true,
            GoogleDefaults.AuthenticationScheme => string.Equals(principal.FindFirstValue(VerifiedEmailClaim), "true", StringComparison.OrdinalIgnoreCase),
            _ => false,
        };

        return verified ? email.Trim() : null;
    }
}

/// <summary>Origins of the configured providers' sign-in pages, for the CSP form-action directive.</summary>
public sealed record ExternalSignInOrigins(IReadOnlyList<string> Origins);
