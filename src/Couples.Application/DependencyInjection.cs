using Microsoft.Extensions.DependencyInjection;

namespace Couples.Application;

public static class DependencyInjection
{
    /// <summary>Registers application-layer services (use cases arrive in later milestones).</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
