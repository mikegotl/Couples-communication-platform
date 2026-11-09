using Couples.Application.Abstractions;
using Couples.Infrastructure.Persistence;
using Couples.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Couples.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Health check tag for checks that need external dependencies (database).</summary>
    public const string ReadyTag = "ready";

    /// <summary>
    /// Registers persistence and infrastructure services. The connection string comes from
    /// configuration or environment (ConnectionStrings__Couples); it is never committed.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string? connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IClock, SystemClock>();

        services.AddDbContext<CouplesDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "couples")));

        services.AddHealthChecks()
            .AddDbContextCheck<CouplesDbContext>("database", tags: [ReadyTag]);

        return services;
    }
}
