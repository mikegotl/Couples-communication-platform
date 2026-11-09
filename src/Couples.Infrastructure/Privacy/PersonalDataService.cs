using Couples.Application.Abstractions;
using Couples.Application.Privacy;
using Couples.Domain.Auditing;
using Couples.Domain.Messaging;
using Couples.Domain.Privacy;
using Couples.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Couples.Infrastructure.Privacy;

internal sealed partial class PersonalDataService(CouplesDbContext db, IClock clock, ILogger<PersonalDataService> logger) : IPersonalDataService
{
    public async Task<PersonalDataExport> ExportAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var memberships = await (
                from m in db.WorkspaceMemberships.AsNoTracking()
                join w in db.Workspaces.AsNoTracking() on m.WorkspaceId equals w.Id
                where m.UserId == userId
                orderby m.JoinedAt
                select new ExportedMembership(w.Id, w.Name, m.Role.ToString(), m.JoinedAt, m.LeftAt))
            .ToListAsync(cancellationToken);

        var reflections = await db.PrivateReflections.AsNoTracking()
            .Where(r => r.OwnerUserId == userId)
            .OrderBy(r => r.CreatedAt)
            .Select(r => new ExportedReflection(r.Id, r.Title, r.Body, r.CreatedAt, r.UpdatedAt))
            .ToListAsync(cancellationToken);

        var drafts = await db.DraftMessages.AsNoTracking()
            .Where(d => d.OwnerUserId == userId)
            .OrderBy(d => d.CreatedAt)
            .Select(d => new ExportedDraft(d.Id, d.Body, d.Status.ToString(), d.UpdatedAt))
            .ToListAsync(cancellationToken);

        var sent = await db.SharedMessages.AsNoTracking()
            .Where(m => m.SenderUserId == userId)
            .OrderBy(m => m.SharedAt)
            .Select(m => new ExportedMessage(m.Id, m.Body, m.SharedAt, m.RevokedAt))
            .ToListAsync(cancellationToken);

        // Received messages follow the same visibility rule as the inbox.
        var received = await db.SharedMessages.AsNoTracking()
            .Where(m => m.RecipientUserId == userId
                && m.RevokedAt == null
                && db.WorkspaceMemberships.Any(r => r.WorkspaceId == m.WorkspaceId && r.UserId == userId && r.LeftAt == null)
                && db.WorkspaceMemberships.Any(s => s.WorkspaceId == m.WorkspaceId && s.UserId == m.SenderUserId && s.LeftAt == null))
            .OrderBy(m => m.SharedAt)
            .Select(m => new ExportedMessage(m.Id, m.Body, m.SharedAt, m.RevokedAt))
            .ToListAsync(cancellationToken);

        var consents = await db.ConsentRecords.AsNoTracking()
            .Where(c => c.UserId == userId)
            .OrderBy(c => c.GrantedAt)
            .Select(c => new ExportedConsent(c.Id, c.Purpose.ToString(), c.SubjectId, c.GrantedAt, c.WithdrawnAt))
            .ToListAsync(cancellationToken);

        var intake = await db.IntakeProfiles.AsNoTracking()
            .Where(p => p.OwnerUserId == userId)
            .Select(p => new ExportedIntake(
                p.Status.ToString(),
                p.CompletedAt,
                p.SkippedAt,
                p.Selections
                    .OrderBy(c => c.CreatedAt)
                    .Select(c => new ExportedConcern(c.Id, c.CategoryKey, c.CustomLabel, c.Summary, c.WarningAcknowledgedAt, c.UpdatedAt))
                    .ToList()))
            .FirstOrDefaultAsync(cancellationToken);

        var attemptRows = await (
                from a in db.ExerciseAttempts.AsNoTracking().Include(a => a.Responses)
                join d in db.ExerciseDefinitions.AsNoTracking() on a.DefinitionId equals d.Id
                where a.OwnerUserId == userId
                orderby a.CreatedAt
                select new { Attempt = a, d.Key, d.Version })
            .ToListAsync(cancellationToken);
        var attempts = attemptRows
            .Select(x => new ExportedExerciseAttempt(
                x.Attempt.Id,
                x.Key,
                x.Version,
                x.Attempt.Status.ToString(),
                x.Attempt.CreatedAt,
                x.Attempt.CompletedAt,
                x.Attempt.Responses.ToDictionary(r => r.StepKey, r => r.Value, StringComparer.Ordinal)))
            .ToList();

        var coaching = await db.CoachingUsage.AsNoTracking()
            .Where(u => u.UserId == userId)
            .OrderBy(u => u.Day)
            .Select(u => new ExportedCoachingUsage(u.Day, u.Requests, u.InputTokens, u.OutputTokens))
            .ToListAsync(cancellationToken);

        var pace = await db.PacePreferences.AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => new ExportedPace(p.QuietStart, p.QuietEnd, p.TimeZoneId, p.Muted, p.PausedUntil))
            .FirstOrDefaultAsync(cancellationToken);
        var notifications = await db.Notifications.AsNoTracking()
            .Where(n => n.RecipientUserId == userId)
            .OrderBy(n => n.CreatedAt)
            .Select(n => new ExportedNotification(n.Kind.ToString(), n.SubjectId, n.VisibleAt, n.ReadAt))
            .ToListAsync(cancellationToken);

        var safetyRow = await db.PersonalSafety.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);
        var safety = safetyRow is null
            ? null
            : new ExportedSafety(
                safetyRow.AnswersJson is null ? null : System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(safetyRow.AnswersJson),
                safetyRow.Level?.ToString(),
                safetyRow.ContentVersion,
                safetyRow.CheckedAt,
                safetyRow.SafetyMode);

        var now = clock.UtcNow;
        db.AuditEvents.Add(AuditEvent.Record(userId, AuditActions.DataExported, null, null, now));
        await db.SaveChangesAsync(cancellationToken);

        return new PersonalDataExport(now, userId, memberships, reflections, drafts, sent, received, consents, intake, attempts, coaching, pace, notifications, safety);
    }

    public async Task<Guid> RequestDeletionAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var existing = await db.DataDeletionRequests
            .FirstOrDefaultAsync(r => r.UserId == userId && r.Status == DeletionStatus.Pending, cancellationToken);
        if (existing is not null)
        {
            return existing.Id;
        }

        var now = clock.UtcNow;
        var request = DataDeletionRequest.Create(userId, now);
        db.DataDeletionRequests.Add(request);
        db.AuditEvents.Add(AuditEvent.Record(userId, AuditActions.DeletionRequested, null, request.Id, now));
        await db.SaveChangesAsync(cancellationToken);
        LogRequested(logger, request.Id);
        return request.Id;
    }

    public async Task<int> ProcessPendingDeletionsAsync(CancellationToken cancellationToken = default)
    {
        var pending = await db.DataDeletionRequests
            .Where(r => r.Status == DeletionStatus.Pending)
            .OrderBy(r => r.RequestedAt)
            .Take(50)
            .ToListAsync(cancellationToken);

        var completed = 0;
        foreach (var request in pending)
        {
            request.RecordAttempt(clock.UtcNow);
            await db.SaveChangesAsync(cancellationToken);

            await EraseAsync(request.UserId, cancellationToken);

            var now = clock.UtcNow;
            request.Complete(now);
            db.AuditEvents.Add(AuditEvent.Record(request.UserId, AuditActions.DeletionCompleted, null, request.Id, now));
            await db.SaveChangesAsync(cancellationToken);
            LogCompleted(logger, request.Id);
            completed++;
        }

        // An account that reappears after its deletion completed (for example when a backup taken
        // before the deletion is restored) is erased again. Requests are kept for 400 days, longer
        // than any backup is retained, so every restorable deletion is covered.
        var reappeared = await db.DataDeletionRequests
            .Where(r => r.Status == DeletionStatus.Completed && db.Users.Any(u => u.Id == r.UserId))
            .Select(r => r.UserId)
            .Distinct()
            .Take(50)
            .ToListAsync(cancellationToken);
        foreach (var userId in reappeared)
        {
            await EraseAsync(userId, cancellationToken);
            LogReErased(logger);
            completed++;
        }

        return completed;
    }

    /// <summary>
    /// Erases every row holding the user's content, then the account. Explicit deletes come
    /// first so nothing depends on cascade behaviour; the account delete cascades whatever is left
    /// (memberships, consent records, messages addressed to the user, Identity tables).
    /// </summary>
    private async Task EraseAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        await db.PrivateReflections.Where(r => r.OwnerUserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.ConcernSelections.Where(c => c.OwnerUserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.IntakeProfiles.Where(p => p.OwnerUserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.ExerciseResponses.Where(r => r.OwnerUserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.ExerciseAttempts.Where(a => a.OwnerUserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.CoachingUsage.Where(u => u.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.PacePreferences.Where(p => p.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.PersonalSafety.Where(s => s.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.Notifications.Where(n => n.RecipientUserId == userId
                || db.SharedMessages.Any(m => m.Id == n.SubjectId && (m.SenderUserId == userId || m.RecipientUserId == userId)))
            .ExecuteDeleteAsync(cancellationToken);
        await db.SharedMessages.Where(m => m.SenderUserId == userId || m.RecipientUserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.DraftMessages.Where(d => d.OwnerUserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.ConsentRecords.Where(c => c.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.Invitations.Where(i => i.CreatedByUserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.Invitations.Where(i => i.AcceptedByUserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.WorkspaceMemberships.Where(m => m.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        // The workspace stays for the partner; only the link to who created it goes.
        await db.Workspaces.Where(w => w.CreatedByUserId == userId)
            .ExecuteUpdateAsync(set => set.SetProperty(w => w.CreatedByUserId, Guid.Empty), cancellationToken);
        await db.Users.Where(u => u.Id == userId).ExecuteDeleteAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Data deletion request {RequestId} recorded")]
    private static partial void LogRequested(ILogger logger, Guid requestId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Re-erased an account whose deletion had already completed (restored from backup?)")]
    private static partial void LogReErased(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Data deletion request {RequestId} completed")]
    private static partial void LogCompleted(ILogger logger, Guid requestId);
}
