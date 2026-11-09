using System.Globalization;
using System.Threading.RateLimiting;
using Couples.Infrastructure.Persistence;
using Couples.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;

namespace Couples.Web.Security;

public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimits";

    /// <summary>All requests from one client address, per minute.</summary>
    public int RequestsPerMinute { get; set; } = 600;

    /// <summary>Form posts to account pages (sign in, register, reset password) from one client address, per minute.</summary>
    public int AccountPostsPerMinute { get; set; } = 10;
}

/// <summary>Transport, key, cookie, request-size and rate-limit settings for the web host (ADR-0013).</summary>
public static class WebSecurity
{
    public static WebApplicationBuilder AddWebSecurity(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var configuration = builder.Configuration;
        var development = builder.Environment.IsDevelopment();

        DatabaseTransportSecurity.Ensure(configuration.GetConnectionString("Couples"), configuration.GetValue(DatabaseTransportSecurity.RequireTlsKey, true));

        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.AddServerHeader = false;
            kestrel.Limits.MaxRequestBodySize = 1_000_000;
            kestrel.Limits.MaxRequestHeadersTotalSize = 32 * 1024;
        });

        // One key ring for every instance, kept across restarts, encrypted at rest outside Development.
        var dataProtection = builder.Services.AddDataProtection()
            .SetApplicationName("BridgeTogether")
            .PersistKeysToDbContext<CouplesDbContext>();
        var certificatePath = configuration["DataProtection:CertificatePath"];
        if (!string.IsNullOrWhiteSpace(certificatePath))
        {
            dataProtection.ProtectKeysWithCertificate(KeyRingCertificate.Load(certificatePath, configuration["DataProtection:CertificatePassword"]));
        }
        else if (!configuration.GetValue<bool>("DataProtection:AllowUnprotectedKeys"))
        {
            throw new InvalidOperationException(
                "Set DataProtection:CertificatePath (and CertificatePassword) so the key ring is encrypted at rest. " +
                "DataProtection:AllowUnprotectedKeys=true is for local development only.");
        }

        // A private pilot needs an invitation code; without one anyone who can register could join.
        if (!development && configuration.GetValue<bool>("FeatureManagement:PilotEnrollmentRequired")
            && string.IsNullOrWhiteSpace(configuration["Pilot:AccessCodeSha256"]))
        {
            throw new InvalidOperationException("Set Pilot:AccessCodeSha256 (SHA-256 hex of the invitation code) while PilotEnrollmentRequired is on.");
        }

        // Behind a TLS-terminating proxy or platform ingress the app sees plain HTTP; trust the
        // proxy's X-Forwarded-* headers so HTTPS, Secure cookies and per-client limits work. Only
        // listed networks are trusted unless the platform guarantees all traffic comes through it.
        var forwarded = configuration.GetSection("ForwardedHeaders");
        if (forwarded.GetValue<bool>("Enabled"))
        {
            builder.Services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                options.ForwardLimit = 1;
                var networks = forwarded.GetSection("KnownNetworks").Get<string[]>() ?? [];
                if (forwarded.GetValue<bool>("TrustAllProxies"))
                {
                    options.KnownIPNetworks.Clear();
                    options.KnownProxies.Clear();
                }

                foreach (var network in networks)
                {
                    options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
                }
            });
        }

        builder.Services.AddAntiforgery(options =>
        {
            options.Cookie.Name = development ? "bt-af" : "__Host-bt-af";
            options.Cookie.SecurePolicy = development ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
        });

        builder.Services.Configure<Microsoft.AspNetCore.Identity.SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(5));

        var limits = configuration.GetSection(RateLimitOptions.SectionName).Get<RateLimitOptions>() ?? new RateLimitOptions();
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, _) =>
            {
                // Sliding windows don't report a retry time; a minute is always enough for them.
                var seconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter) ? (int)Math.Ceiling(retryAfter.TotalSeconds) : 60;
                context.HttpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);

                return ValueTask.CompletedTask;
            };
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var request = context.Request;
                if (request.Path.StartsWithSegments("/health"))
                {
                    return RateLimitPartition.GetNoLimiter("health");
                }

                var client = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                if (HttpMethods.IsPost(request.Method) && request.Path.StartsWithSegments("/Account"))
                {
                    return RateLimitPartition.GetSlidingWindowLimiter("account:" + client, _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = limits.AccountPostsPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        SegmentsPerWindow = 6,
                        QueueLimit = 0,
                    });
                }

                return RateLimitPartition.GetFixedWindowLimiter("all:" + client, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limits.RequestsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                });
            });
        });

        return builder;
    }
}
