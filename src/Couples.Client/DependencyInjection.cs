using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Couples.Client;

public static class DependencyInjection
{
    /// <summary>Registers <see cref="BridgeTogetherApiClient"/> against the configured server.</summary>
    public static IServiceCollection AddBridgeTogetherApiClient(this IServiceCollection services, Action<BridgeTogetherApiOptions> configure)
    {
        services.AddOptions<BridgeTogetherApiOptions>()
            .Configure(configure)
            .Validate(o => o.BaseAddress is { IsAbsoluteUri: true }, "BridgeTogether API base address must be an absolute URI.")
            .Validate(o => o.BaseAddress is null || o.BaseAddress.Scheme == Uri.UriSchemeHttps || o.AllowInsecureHttp,
                "BridgeTogether API base address must use https.")
            .ValidateOnStart();

        services.AddHttpClient<BridgeTogetherApiClient>((provider, http) =>
        {
            var options = provider.GetRequiredService<IOptions<BridgeTogetherApiOptions>>().Value;
            var baseAddress = options.BaseAddress!;
            // A trailing slash keeps relative paths like "api/v1/..." under any path prefix.
            http.BaseAddress = baseAddress.AbsoluteUri.EndsWith('/') ? baseAddress : new Uri(baseAddress.AbsoluteUri + "/");
            http.Timeout = TimeSpan.FromSeconds(20);
        });

        return services;
    }
}
