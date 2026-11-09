using Couples.Application.Abstractions;
using Couples.Application.Privacy;
using Couples.Domain.Messaging;
using Couples.Domain.Privacy;
using Couples.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Couples.Infrastructure.Privacy;

internal sealed partial class RetentionService(
    CouplesDbContext db,
    IClock clock,
    IOptions<RetentionOptions> options,
    ILogger<RetentionService> logger) : IRetentionService
{
    public async Task<RetentionReport> ApplyAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var o = options.Value;

        var invitationCutoff = now - o.ClosedInvitations;
        var invitations = await db.Invitations
            .Where(i => (i.AcceptedAt != null && i.AcceptedAt < invitationCutoff)
                || (i.RevokedAt != null && i.RevokedAt < invitationCutoff)
                || i.ExpiresAt < invitationCutoff)
            .ExecuteDeleteAsync(cancellationToken);

        var draftCutoff = now - o.DiscardedDrafts;
        var drafts = await db.DraftMessages
            .Where(d => d.Status == DraftStatus.Discarded && d.DiscardedAt < draftCutoff)
            .ExecuteDeleteAsync(cancellationToken);

        // A workspace with no active members whose last member left before the cutoff. Deleting it
        // cascades its memberships, invitations, drafts and shared messages.
        var workspaceCutoff = now - o.EmptyWorkspaces;
        var workspaces = await db.Workspaces
            .Where(w => !w.Memberships.Any(m => m.LeftAt == null)
                && w.Memberships.All(m => m.LeftAt < workspaceCutoff))
            .ExecuteDeleteAsync(cancellationToken);

        var auditCutoff = now - o.AuditEvents;
        var audits = await db.AuditEvents.Where(a => a.OccurredAt < auditCutoff).ExecuteDeleteAsync(cancellationToken);

        var requestCutoff = now - o.CompletedDeletionRequests;
        var requests = await db.DataDeletionRequests
            .Where(r => r.Status == DeletionStatus.Completed && r.CompletedAt < requestCutoff)
            .ExecuteDeleteAsync(cancellationToken);

        var usageCutoff = DateOnly.FromDateTime((now - o.CoachingUsage).UtcDateTime);
        var usage = await db.CoachingUsage.Where(u => u.Day < usageCutoff).ExecuteDeleteAsync(cancellationToken);

        var notificationCutoff = now - o.Notifications;
        var notifications = await db.Notifications.Where(n => n.CreatedAt < notificationCutoff).ExecuteDeleteAsync(cancellationToken);

        var feedbackCutoff = now - o.Feedback;
        var feedback = await db.Feedback.Where(f => f.CreatedAt < feedbackCutoff).ExecuteDeleteAsync(cancellationToken);

        // Ended joint chats are readable for a short time, then deleted with their transcript.
        var chatCutoff = now - Couples.Domain.JointChat.JointChatPolicy.TranscriptKeptAfterEnd;
        var endedChats = db.JointChatSessions.Where(c => c.Status == Couples.Domain.JointChat.JointChatStatus.Ended && c.EndedAt < chatCutoff);
        await db.JointChatMessages.Where(m => endedChats.Any(c => c.Id == m.SessionId)).ExecuteDeleteAsync(cancellationToken);
        var jointChats = await endedChats.ExecuteDeleteAsync(cancellationToken);

        var report = new RetentionReport(invitations, drafts, workspaces, audits, requests, usage, notifications, feedback, jointChats);
        LogApplied(logger, invitations, drafts, workspaces, audits, requests, usage, notifications, feedback, jointChats);
        return report;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Retention purged {Invitations} invitations, {Drafts} discarded drafts, {Workspaces} empty workspaces, {Audits} audit events, {Requests} deletion requests, {UsageDays} coaching usage days, {Notifications} notifications, {Feedback} feedback entries, {JointChats} ended joint chats")]
    private static partial void LogApplied(ILogger logger, int invitations, int drafts, int workspaces, int audits, int requests, int usageDays, int notifications, int feedback, int jointChats);
}
