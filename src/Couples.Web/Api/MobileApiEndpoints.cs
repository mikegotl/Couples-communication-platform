using Couples.Application.Mobile;
using Microsoft.Extensions.Options;

namespace Couples.Web.Api;

/// <summary>
/// The versioned JSON API the iPhone app talks to (docs/adr/0020). The web pages don't use it; they
/// call Application services in-process. Rules for every endpoint added here:
/// the user is always taken from the authenticated principal, never from the request body or route;
/// responses carry no data a page wouldn't show the same user; and each endpoint calls the same
/// Application service the matching page uses, so privacy, consent and safety checks stay in one place.
/// </summary>
public static class MobileApiEndpoints
{
    public static IServiceCollection AddMobileApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MobileAppOptions>(configuration.GetSection(MobileAppOptions.SectionName));
        return services;
    }

    public static IEndpointRouteBuilder MapMobileApi(this IEndpointRouteBuilder endpoints)
    {
        var v1 = endpoints.MapGroup("/api/v1");

        // Anonymous and user-free: lets the app check it reached BridgeTogether and is still supported.
        v1.MapGet("/app-status", (IOptions<MobileAppOptions> options) =>
                TypedResults.Ok(new AppStatus("BridgeTogether", AppStatus.CurrentApiVersion, options.Value.MinimumVersion)))
            .AllowAnonymous();

        return endpoints;
    }
}
