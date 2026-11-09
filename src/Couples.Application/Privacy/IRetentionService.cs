namespace Couples.Application.Privacy;

/// <summary>Applies the retention rules declared on each entity (see docs/privacy/data-inventory.md).</summary>
public interface IRetentionService
{
    Task<RetentionReport> ApplyAsync(CancellationToken cancellationToken = default);
}

public sealed record RetentionReport(int Invitations, int DiscardedDrafts, int EmptyWorkspaces, int AuditEvents, int DeletionRequests, int CoachingUsageDays = 0, int Notifications = 0, int Feedback = 0);

public sealed class RetentionOptions
{
    public const string SectionName = "Retention";

    public TimeSpan ClosedInvitations { get; set; } = TimeSpan.FromDays(30);

    public TimeSpan DiscardedDrafts { get; set; } = TimeSpan.FromDays(30);

    public TimeSpan EmptyWorkspaces { get; set; } = TimeSpan.FromDays(30);

    public TimeSpan AuditEvents { get; set; } = TimeSpan.FromDays(400);

    public TimeSpan CompletedDeletionRequests { get; set; } = TimeSpan.FromDays(400);

    public TimeSpan CoachingUsage { get; set; } = TimeSpan.FromDays(90);

    public TimeSpan Notifications { get; set; } = TimeSpan.FromDays(90);

    public TimeSpan Feedback { get; set; } = TimeSpan.FromDays(365);
}
