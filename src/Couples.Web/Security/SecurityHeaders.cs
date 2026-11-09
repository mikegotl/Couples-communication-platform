using System.Security.Cryptography;

namespace Couples.Web.Security;

/// <summary>
/// Adds security headers to every response: a nonce-based Content Security Policy, no framing,
/// no referrer (so "Leave quickly" and outbound support links don't reveal this site), no
/// sniffing, a locked-down Permissions-Policy, and <c>no-store</c> on dynamic responses so
/// private pages aren't kept in shared or browser caches.
/// </summary>
public static class SecurityHeaders
{
    private const string NonceKey = "csp-nonce";

    /// <summary>The per-request nonce for the few inline scripts the framework renders (the import map).</summary>
    public static string GetCspNonce(this HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Items[NonceKey] is not string nonce)
        {
            nonce = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
            context.Items[NonceKey] = nonce;
        }

        return nonce;
    }

    public static string ContentSecurityPolicy(string nonce, bool upgradeInsecure) =>
        "default-src 'self'; " +
        $"script-src 'self' 'nonce-{nonce}'; " +
        // MudBlazor sets inline style attributes; no inline <style> or script is needed beyond that.
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; font-src 'self'; connect-src 'self'; " +
        "object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'; " +
        "manifest-src 'self'; worker-src 'self'" +
        (upgradeInsecure ? "; upgrade-insecure-requests" : string.Empty);

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app, bool upgradeInsecure)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.Use(async (context, next) =>
        {
            var nonce = context.GetCspNonce();
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers.ContentSecurityPolicy = ContentSecurityPolicy(nonce, upgradeInsecure);
                headers.XContentTypeOptions = "nosniff";
                headers.XFrameOptions = "DENY";
                headers["Referrer-Policy"] = "no-referrer";
                headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=(), interest-cohort=()";
                headers["Cross-Origin-Opener-Policy"] = "same-origin";
                headers["Cross-Origin-Resource-Policy"] = "same-origin";
                headers["X-Permitted-Cross-Domain-Policies"] = "none";
                if (string.IsNullOrEmpty(headers.CacheControl))
                {
                    headers.CacheControl = "no-store";
                }

                headers.Remove("Server");
                return Task.CompletedTask;
            });

            await next(context);
        });
    }
}
