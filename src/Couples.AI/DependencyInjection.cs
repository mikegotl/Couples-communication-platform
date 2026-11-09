using Couples.Application.Coaching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Couples.AI;

public static partial class DependencyInjection
{
    /// <summary>
    /// Registers the coach. The provider comes from <c>Coaching:Provider</c> (Disabled by
    /// default). The live gateway is used only when it is fully configured and the owner has set
    /// <c>Coaching:ApprovedForUserContent</c>; otherwise the coach behaves as Disabled.
    /// Requires an <see cref="ICoachUsageStore"/> (registered by the infrastructure layer).
    /// </summary>
    public static IServiceCollection AddCoaching(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<CoachingOptions>().BindConfiguration(CoachingOptions.SectionName);
        services.AddMetrics();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<CoachTelemetry>();
        services.AddSingleton<FakeCoachModelClient>();
        services.AddHttpClient<GatewayCoachModelClient>(client => client.Timeout = Timeout.InfiniteTimeSpan);

        services.AddScoped<ICommunicationCoach>(sp =>
        {
            var o = sp.GetRequiredService<IOptions<CoachingOptions>>().Value;
            var logger = sp.GetRequiredService<ILogger<CoachService>>();
            ICoachModelClient? model = o.Provider switch
            {
                CoachProvider.Fake => sp.GetRequiredService<FakeCoachModelClient>(),
                CoachProvider.Gateway when o.ApprovedForUserContent && o.Gateway.IsConfigured => sp.GetRequiredService<GatewayCoachModelClient>(),
                _ => null,
            };

            if (o.Provider == CoachProvider.Gateway && model is null)
            {
                LogGatewayNotReady(logger);
            }

            return new CoachService(
                model,
                sp.GetRequiredService<ICoachUsageStore>(),
                sp.GetRequiredService<IOptions<CoachingOptions>>(),
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<CoachTelemetry>(),
                logger);
        });

        return services;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Coaching provider is Gateway but it is not approved for user content or not fully configured; the coach is disabled.")]
    private static partial void LogGatewayNotReady(ILogger logger);
}
