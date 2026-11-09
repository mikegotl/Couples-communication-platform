using Microsoft.Extensions.DependencyInjection.Extensions;
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
        services.AddScoped<Couples.Application.Inbox.IInboxService, Couples.Infrastructure.Inbox.InboxService>();
        services.AddScoped<Couples.Application.Pace.IPaceService, Couples.Infrastructure.Pace.PaceService>();
        services.AddScoped<Couples.Application.Notifications.INotificationService, Couples.Infrastructure.Pace.NotificationService>();
        services.AddMemoryCache();
        services.AddOptions<Couples.Application.Pilot.PilotOptions>().BindConfiguration(Couples.Application.Pilot.PilotOptions.SectionName);
        services.AddScoped<Couples.Application.Pilot.IPilotService, Couples.Infrastructure.Pilot.PilotService>();
        services.AddScoped<Couples.Application.Feedback.IFeedbackService, Couples.Infrastructure.Feedback.FeedbackService>();
        services.AddScoped<Couples.Application.Observability.IOperationsOverview, Couples.Infrastructure.Observability.OperationsOverview>();
        services.AddScoped<Couples.Infrastructure.Safety.SafetyService>();
        services.AddScoped<Couples.Application.Safety.ISafetyService>(sp => sp.GetRequiredService<Couples.Infrastructure.Safety.SafetyService>());
        services.AddScoped<Couples.Application.Safety.ISafetyGate>(sp => sp.GetRequiredService<Couples.Infrastructure.Safety.SafetyService>());
        services.AddOptions<Couples.Application.Exercises.ExerciseOptions>().BindConfiguration(Couples.Application.Exercises.ExerciseOptions.SectionName);
        services.AddScoped<Couples.Infrastructure.Exercises.ExerciseCatalogSync>();
        services.AddScoped<Couples.Application.Exercises.IExerciseService, Couples.Infrastructure.Exercises.ExerciseService>();
        services.AddScoped<Couples.Application.Exercises.IExerciseAdminService, Couples.Infrastructure.Exercises.ExerciseAdminService>();
        services.AddScoped<IPersonalDataService, PersonalDataService>();
        services.AddScoped<Couples.Application.Coaching.ICoachUsageStore, Couples.Infrastructure.Coaching.CoachUsageStore>();
        services.AddOptions<RetentionOptions>().BindConfiguration(RetentionOptions.SectionName);
        services.AddScoped<IRetentionService, RetentionService>();

        // Phase 2 joint chat: closed unless the Phase2JointChat flag is on and both sign-offs are set.
        services.TryAddSingleton<IFeatureFlags, ConfigurationFeatureFlags>();
        services.AddOptions<Couples.Application.JointChat.JointChatOptions>().BindConfiguration(Couples.Application.JointChat.JointChatOptions.SectionName);
        services.AddSingleton<Couples.Application.JointChat.IJointChatNotifier, Couples.Infrastructure.JointChat.JointChatNotifier>();
        services.AddScoped<Couples.Application.JointChat.IJointChatService, Couples.Infrastructure.JointChat.JointChatService>();
        services.AddScoped<Couples.Application.JointChat.IJointChatAdminService, Couples.Infrastructure.JointChat.JointChatAdminService>();

        services.AddHealthChecks()
            .AddDbContextCheck<CouplesDbContext>("database", tags: [ReadyTag]);

        return services;
    }

    internal static void ConfigureNpgsql(DbContextOptionsBuilder options, string? connectionString) =>
        options.UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsHistoryTable("__ef_migrations_history", CouplesDbContext.Schema));
}
