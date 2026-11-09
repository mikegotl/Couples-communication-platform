using System.Security.Claims;
using Couples.Application;
using Couples.Application.Pilot;
using Microsoft.FeatureManagement;

namespace Couples.Web.Security;

/// <summary>
/// While <c>PilotEnrollmentRequired</c> is on, a signed-in user who hasn't agreed to the current
/// pilot terms and privacy notice can reach only their account, the pilot page, the legal and
/// support pages. Everything else, including the interactive (SignalR) connection that every
/// feature runs on, is refused on the server (ADR-0014).
/// </summary>
public static class PilotGate
{
    private static readonly string[] OpenPrefixes =
    [
        "/pilot", "/Account", "/support", "/privacy", "/privacy-notice", "/terms", "/health",
        "/not-found", "/Error", "/_framework", "/_content", "/js", "/favicon",
    ];

    public static bool IsOpen(PathString path) =>
        path == "/" || OpenPrefixes.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)) || Path.HasExtension(path.Value);

    public static IApplicationBuilder UsePilotGate(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.Use(async (context, next) =>
        {
            if (context.User.Identity?.IsAuthenticated != true || IsOpen(context.Request.Path))
            {
                await next(context);
                return;
            }

            var features = context.RequestServices.GetRequiredService<IFeatureManager>();
            if (!await features.IsEnabledAsync(FeatureFlags.PilotEnrollmentRequired)
                || !Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            {
                await next(context);
                return;
            }

            // Its own scope: this runs before the endpoint, which has its own DbContext uses.
            await using (var scope = context.RequestServices.CreateAsyncScope())
            {
                if (await scope.ServiceProvider.GetRequiredService<IPilotService>().IsEnrolledAsync(userId, context.RequestAborted))
                {
                    await next(context);
                    return;
                }
            }

            if (HttpMethods.IsGet(context.Request.Method) && !context.Request.Path.StartsWithSegments("/_blazor"))
            {
                var returnUrl = Uri.EscapeDataString(context.Request.Path + context.Request.QueryString);
                context.Response.Redirect("/pilot?returnUrl=" + returnUrl);
                return;
            }

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
        });
    }
}
