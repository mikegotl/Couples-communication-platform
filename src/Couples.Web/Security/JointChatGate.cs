using Couples.Application;
using Couples.Application.Abstractions;
using Microsoft.FeatureManagement;

namespace Couples.Web.Security;

/// <summary>
/// While <c>Phase2JointChat</c> is off, the joint chat pages don't exist: every request to them,
/// signed in or not, gets 404 before any page or service runs. The services check the flag
/// again themselves (ADR-0015).
/// </summary>
public static class JointChatGate
{
    private static readonly string[] Paths = ["/joint-chat", "/admin/joint-chat"];

    public static bool IsJointChatPath(PathString path) =>
        Paths.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase));

    public static IApplicationBuilder UseJointChatGate(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.Use(async (context, next) =>
        {
            if (IsJointChatPath(context.Request.Path)
                && !await context.RequestServices.GetRequiredService<IFeatureManager>().IsEnabledAsync(FeatureFlags.Phase2JointChat))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            await next(context);
        });
    }
}

/// <summary>Gives services below the web layer the same flag values the UI sees.</summary>
internal sealed class FeatureManagerFlags(IFeatureManager features) : IFeatureFlags
{
    public Task<bool> IsEnabledAsync(string flag, CancellationToken cancellationToken = default) =>
        features.IsEnabledAsync(flag);
}
