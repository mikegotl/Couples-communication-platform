using Microsoft.Extensions.DependencyInjection;

namespace Couples.AI;

public static class DependencyInjection
{
    /// <summary>
    /// Registers AI coaching services. The provider-neutral ICommunicationCoach, its fake
    /// implementation and safeguards are delivered in milestone 06. No provider is called
    /// and no user text leaves the process in this milestone.
    /// </summary>
    public static IServiceCollection AddCoaching(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
