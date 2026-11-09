using Couples.Application.Abstractions;
using Couples.Application.Messaging;
using Couples.Application.Privacy;
using Couples.Application.Reflections;
using Couples.Application.Workspaces;
using Couples.Infrastructure.Messaging;
using Couples.Infrastructure.Privacy;
using Couples.Infrastructure.Reflections;
using Couples.Infrastructure.Persistence;
using Couples.Infrastructure.Time;
using Couples.Infrastructure.Workspaces;
using Microsoft.AspNetCore.Identity;
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

        // Identity store shape is part of the EF model, so it is fixed here rather than in each
        // host. The web app, the worker, tests and `dotnet ef` then all build the same model.
        services.Configure<IdentityOptions>(options =>
        {
            options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
            options.Stores.MaxLengthForKeys = 128;
        });

        services.AddDbContext<CouplesDbContext>(options => ConfigureNpgsql(options, connectionString));

        services.AddOptions<WorkspaceOptions>().BindConfiguration(WorkspaceOptions.SectionName);
        services.AddScoped<IWorkspaceService, WorkspaceService>();
        services.AddScoped<IPrivateReflectionService, PrivateReflectionService>();
        services.AddScoped<Couples.Application.Intake.IIntakeService, Couples.Infrastructure.Intake.IntakeService>();
        services.AddScoped<IMessagingService, MessagingService>();
        services.AddScoped<IPersonalDataService, PersonalDataService>();
        services.AddOptions<RetentionOptions>().BindConfiguration(RetentionOptions.SectionName);
        services.AddScoped<IRetentionService, RetentionService>();

        services.AddHealthChecks()
            .AddDbContextCheck<CouplesDbContext>("database", tags: [ReadyTag]);

        return services;
    }

    internal static void ConfigureNpgsql(DbContextOptionsBuilder options, string? connectionString) =>
        options.UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsHistoryTable("__ef_migrations_history", CouplesDbContext.Schema));
}
