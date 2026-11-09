using Couples.Domain.Auditing;
using Couples.Domain.Coaching;
using Couples.Domain.Common;
using Couples.Domain.Consent;
using Couples.Domain.Exercises;
using Couples.Domain.Intake;
using Couples.Domain.Messaging;
using Couples.Domain.Privacy;
using Couples.Domain.Reflections;
using Couples.Domain.Workspaces;
using Couples.Infrastructure.Identity;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Couples.Infrastructure.Persistence;

/// <summary>
/// Primary EF Core context: ASP.NET Core Identity tables plus domain entities, all in the
/// <c>couples</c> schema.
/// </summary>
public sealed class CouplesDbContext(DbContextOptions<CouplesDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options), IDataProtectionKeyContext
{
    public const string Schema = "couples";

    public DbSet<RelationshipWorkspace> Workspaces => Set<RelationshipWorkspace>();

    public DbSet<WorkspaceMembership> WorkspaceMemberships => Set<WorkspaceMembership>();

    public DbSet<Invitation> Invitations => Set<Invitation>();

    public DbSet<PrivateReflection> PrivateReflections => Set<PrivateReflection>();

    public DbSet<DraftMessage> DraftMessages => Set<DraftMessage>();

    public DbSet<SharedMessage> SharedMessages => Set<SharedMessage>();

    public DbSet<ConsentRecord> ConsentRecords => Set<ConsentRecord>();

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    public DbSet<DataDeletionRequest> DataDeletionRequests => Set<DataDeletionRequest>();

    public DbSet<ConcernCategory> ConcernCategories => Set<ConcernCategory>();

    public DbSet<IntakeProfile> IntakeProfiles => Set<IntakeProfile>();

    public DbSet<ConcernSelection> ConcernSelections => Set<ConcernSelection>();

    public DbSet<ExerciseDefinition> ExerciseDefinitions => Set<ExerciseDefinition>();

    public DbSet<ExerciseAttempt> ExerciseAttempts => Set<ExerciseAttempt>();

    public DbSet<ExerciseResponse> ExerciseResponses => Set<ExerciseResponse>();

    public DbSet<CoachingUsage> CoachingUsage => Set<CoachingUsage>();

    public DbSet<Couples.Domain.Pace.PacePreferences> PacePreferences => Set<Couples.Domain.Pace.PacePreferences>();

    public DbSet<Couples.Domain.Notifications.Notification> Notifications => Set<Couples.Domain.Notifications.Notification>();

    public DbSet<Couples.Domain.Safety.PersonalSafety> PersonalSafety => Set<Couples.Domain.Safety.PersonalSafety>();

    public DbSet<Couples.Domain.Pilot.PilotEnrollment> PilotEnrollments => Set<Couples.Domain.Pilot.PilotEnrollment>();

    public DbSet<Couples.Domain.Feedback.FeedbackEntry> Feedback => Set<Couples.Domain.Feedback.FeedbackEntry>();

    public DbSet<Couples.Domain.JointChat.JointChatEligibility> JointChatEligibility => Set<Couples.Domain.JointChat.JointChatEligibility>();

    public DbSet<Couples.Domain.JointChat.JointChatSession> JointChatSessions => Set<Couples.Domain.JointChat.JointChatSession>();

    public DbSet<Couples.Domain.JointChat.JointChatMessage> JointChatMessages => Set<Couples.Domain.JointChat.JointChatMessage>();

    /// <summary>
    /// ASP.NET Core Data Protection key ring (cookies, antiforgery and account tokens). Shared by
    /// every web instance and kept across restarts; encrypted with a certificate outside
    /// Development (see ADR-0013).
    /// </summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.OnModelCreating(builder);
        builder.HasDefaultSchema(Schema);

        builder.ApplyConfigurationsFromAssembly(typeof(CouplesDbContext).Assembly);
        builder.Entity<DataProtectionKey>().ToTable("data_protection_keys");

        // Domain entities create their own ids (UUIDv7). Telling EF the key is never generated
        // makes it insert children added through a navigation instead of treating them as
        // existing rows to update.
        foreach (var entityType in builder.Model.GetEntityTypes().Where(t => typeof(Entity).IsAssignableFrom(t.ClrType)))
        {
            builder.Entity(entityType.ClrType).Property(nameof(Entity.Id)).ValueGeneratedNever();
        }
    }
}
