using Couples.Application;
using Couples.Application.Abstractions;
using Couples.Application.JointChat;
using Couples.Application.Observability;
using Couples.Application.Safety;
using Couples.Domain.Auditing;
using Couples.Domain.Common;
using Couples.Domain.Consent;
using Couples.Domain.JointChat;
using Couples.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Couples.Infrastructure.JointChat;

/// <summary>
/// Joint chat (ADR-0015). Every entry point re-checks the flag, sign-offs, eligibility,
/// membership and the safety gate against the database; nothing is trusted from the page. Logs
/// nothing about chats: no text, and no reasons, so a safety hold leaves no trace.
/// </summary>
internal sealed class JointChatService(
    CouplesDbContext db,
    IClock clock,
    IFeatureFlags flags,
    IOptions<JointChatOptions> options,
    ISafetyGate safety,
    IJointChatNotifier notifier) : IJointChatService
{
    private sealed record Context(Guid WorkspaceId, Guid PartnerUserId);

    /// <summary>
    /// A page keeps one DbContext for as long as it is open, while the other partner changes the
    /// same chat from their own page. Start every operation from the database, never from an
    /// entity tracked earlier, so turn and pause state can't be stale.
    /// </summary>
    private void Fresh() => db.ChangeTracker.Clear();

    public async Task<bool> IsOfferedAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await ContextAsync(userId, cancellationToken) is not null;

    public async Task<JointChatView> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        Fresh();
        var context = await ContextAsync(userId, cancellationToken);
        if (context is null)
        {
            await EndOrphanedAsync(userId, cancellationToken);
            return JointChatView.Of(JointChatState.Unavailable);
        }

        var session = await OpenSessionAsync(context.WorkspaceId, cancellationToken);
        if (await safety.UserBlocksJointFlowsAsync(userId, cancellationToken))
        {
            await EndQuietlyAsync(session, cancellationToken);
            return JointChatView.Of(JointChatState.HeldForYou);
        }

        if (session is not null && await RefreshAsync(session, context, cancellationToken))
        {
            await SaveAndNotifyAsync(session, cancellationToken);
        }

        return await ViewAsync(userId, context, cancellationToken);
    }

    public async Task<Result<JointChatView>> StartAsync(Guid userId, string policyVersion, CancellationToken cancellationToken = default)
    {
        Fresh();
        var context = await ContextAsync(userId, cancellationToken);
        if (context is null)
        {
            return DomainError.NotFound;
        }

        if (await safety.UserBlocksJointFlowsAsync(userId, cancellationToken))
        {
            return DomainError.SafetyHold;
        }

        var open = await OpenSessionAsync(context.WorkspaceId, cancellationToken);
        if (open is not null && await RefreshAsync(open, context, cancellationToken))
        {
            await SaveAndNotifyAsync(open, cancellationToken);
        }

        if (open is { Status: not JointChatStatus.Ended })
        {
            return DomainError.Conflict;
        }

        var now = clock.UtcNow;
        var invited = JointChatSession.Invite(context.WorkspaceId, userId, context.PartnerUserId, policyVersion, now);
        if (!invited.Succeeded)
        {
            return invited.Error!.Value;
        }

        // The invitation is created even if the partner's own safety check keeps joint chat off
        // for them. They never see it, and it lapses like any unanswered invitation, so the
        // person inviting learns nothing about their partner's check-in.
        var session = invited.Value;
        db.JointChatSessions.Add(session);
        RecordConsent(userId, session);
        db.AuditEvents.Add(AuditEvent.Record(userId, AuditActions.JointChatInvited, context.WorkspaceId, session.Id, now));
        var saved = await SaveAsync(cancellationToken);
        if (!saved.Succeeded)
        {
            return saved.Error!.Value;
        }

        CouplesMetrics.JointChatSessions.Add(1, new KeyValuePair<string, object?>("action", "started"));
        notifier.Notify(session.Id);
        return Result.Ok(await ViewAsync(userId, context, cancellationToken));
    }

    public async Task<Result<JointChatView>> JoinAsync(Guid userId, Guid sessionId, string policyVersion, CancellationToken cancellationToken = default)
    {
        Fresh();
        var (context, session, error) = await ParticipantSessionAsync(userId, sessionId, cancellationToken);
        if (error is not null)
        {
            return error.Value;
        }

        if (session!.Status != JointChatStatus.Invited || session.InviteeUserId != userId)
        {
            return DomainError.Conflict;
        }

        if (!await safety.WorkspaceAllowsJointFlowsAsync(session.WorkspaceId, cancellationToken))
        {
            // The partner's own check-in now keeps joint flows off: the chat never starts, and
            // this person sees only that it has ended.
            session.EndUnavailable(clock.UtcNow);
            await SaveAndNotifyAsync(session, cancellationToken);
            return DomainError.Conflict;
        }

        var accepted = session.Accept(userId, policyVersion, clock.UtcNow);
        if (!accepted.Succeeded)
        {
            return accepted.Error!.Value;
        }

        RecordConsent(userId, session);
        db.AuditEvents.Add(AuditEvent.Record(userId, AuditActions.JointChatJoined, session.WorkspaceId, session.Id, clock.UtcNow));
        CouplesMetrics.JointChatSessions.Add(1, new KeyValuePair<string, object?>("action", "joined"));
        return await SaveAndViewAsync(userId, context!, session, cancellationToken);
    }

    public async Task<Result<JointChatView>> DeclineAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken = default)
    {
        Fresh();
        var (context, session, error) = await ParticipantSessionAsync(userId, sessionId, cancellationToken, requireSafe: false);
        if (error is not null)
        {
            return error.Value;
        }

        var declined = session!.Decline(userId, clock.UtcNow);
        if (!declined.Succeeded)
        {
            return declined.Error!.Value;
        }

        db.AuditEvents.Add(AuditEvent.Record(userId, AuditActions.JointChatEnded, session.WorkspaceId, session.Id, clock.UtcNow));
        return await SaveAndViewAsync(userId, context!, session, cancellationToken);
    }

    public async Task<Result<JointChatView>> LeaveAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken = default)
    {
        Fresh();
        // Leaving needs only the pilot switched on and the person to be in the chat: no
        // eligibility, membership or safety condition can stop someone from leaving.
        if (!await flags.IsEnabledAsync(FeatureFlags.Phase2JointChat, cancellationToken))
        {
            return DomainError.NotFound;
        }

        var session = await db.JointChatSessions.FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
        if (session is null || !session.Involves(userId))
        {
            return DomainError.NotFound;
        }

        var left = session.Leave(userId, clock.UtcNow);
        if (left.Succeeded)
        {
            db.AuditEvents.Add(AuditEvent.Record(userId, AuditActions.JointChatEnded, session.WorkspaceId, session.Id, clock.UtcNow));
            var saved = await SaveAsync(cancellationToken);
            if (!saved.Succeeded)
            {
                return saved.Error!.Value;
            }

            notifier.Notify(session.Id);
        }

        var context = await ContextAsync(userId, cancellationToken);
        return Result.Ok(context is null ? JointChatView.Of(JointChatState.Unavailable) : await ViewAsync(userId, context, cancellationToken));
    }

    public async Task<Result<JointChatView>> PauseAsync(Guid userId, Guid sessionId, int minutes, CancellationToken cancellationToken = default)
    {
        Fresh();
        var (context, session, error) = await ParticipantSessionAsync(userId, sessionId, cancellationToken);
        if (error is not null)
        {
            return error.Value;
        }

        var paused = session!.Pause(userId, minutes, clock.UtcNow);
        return paused.Succeeded ? await SaveAndViewAsync(userId, context!, session, cancellationToken) : paused.Error!.Value;
    }

    public async Task<Result<JointChatView>> ResumeAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken = default)
    {
        Fresh();
        var (context, session, error) = await ParticipantSessionAsync(userId, sessionId, cancellationToken);
        if (error is not null)
        {
            return error.Value;
        }

        var resumed = session!.Resume(userId, clock.UtcNow);
        return resumed.Succeeded ? await SaveAndViewAsync(userId, context!, session, cancellationToken) : resumed.Error!.Value;
    }

    public async Task<Result<JointChatSendResult>> SendAsync(Guid userId, Guid sessionId, string? text, CancellationToken cancellationToken = default)
    {
        Fresh();
        var (context, session, error) = await ParticipantSessionAsync(userId, sessionId, cancellationToken);
        if (error is not null)
        {
            return error.Value;
        }

        var now = clock.UtcNow;
        var created = JointChatMessage.Create(session!.Id, userId, text, now);
        if (!created.Succeeded)
        {
            return created.Error!.Value;
        }

        if (!session.CanWrite(userId, now))
        {
            return DomainError.Conflict;
        }

        var outcome = JointChatModeration.Check(created.Value.Body);
        switch (outcome)
        {
            case ModerationOutcome.HeldToRephrase:
                // Nothing is stored: the text stays in the writer's box for them to change.
                CouplesMetrics.JointChatMessages.Add(1, new KeyValuePair<string, object?>("outcome", "not_delivered"));
                return Result.Ok(new JointChatSendResult(outcome, await ViewAsync(userId, context!, cancellationToken)));
            case ModerationOutcome.EndedForSafety:
                // Not delivered and not stored. The chat ends neutrally for both; only the writer
                // is shown support, by the page, from this result.
                session.EndUnavailable(now);
                CouplesMetrics.JointChatMessages.Add(1, new KeyValuePair<string, object?>("outcome", "not_delivered"));
                await SaveAndNotifyAsync(session, cancellationToken);
                return Result.Ok(new JointChatSendResult(outcome, await ViewAsync(userId, context!, cancellationToken)));
            default:
                session.RecordMessage(userId, now);
                db.JointChatMessages.Add(created.Value);
                var saved = await SaveAsync(cancellationToken);
                if (!saved.Succeeded)
                {
                    return saved.Error!.Value;
                }

                CouplesMetrics.JointChatMessages.Add(1, new KeyValuePair<string, object?>("outcome", "delivered"));
                notifier.Notify(session.Id);
                return Result.Ok(new JointChatSendResult(outcome, await ViewAsync(userId, context!, cancellationToken)));
        }
    }

    /// <summary>
    /// The user's workspace and partner, if joint chat is offered to them at all: flag on, both
    /// sign-offs recorded, an active two-person workspace with current eligibility.
    /// </summary>
    private async Task<Context?> ContextAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (!options.Value.SignedOff || !await flags.IsEnabledAsync(FeatureFlags.Phase2JointChat, cancellationToken))
        {
            return null;
        }

        var workspaceId = await db.WorkspaceMemberships
            .Where(m => m.UserId == userId && m.LeftAt == null)
            .Select(m => (Guid?)m.WorkspaceId)
            .FirstOrDefaultAsync(cancellationToken);
        if (workspaceId is null
            || !await db.JointChatEligibility.AnyAsync(e => e.WorkspaceId == workspaceId && e.RevokedAt == null, cancellationToken))
        {
            return null;
        }

        var partner = await db.WorkspaceMemberships
            .Where(m => m.WorkspaceId == workspaceId && m.LeftAt == null && m.UserId != userId)
            .Select(m => (Guid?)m.UserId)
            .FirstOrDefaultAsync(cancellationToken);
        return partner is null ? null : new Context(workspaceId.Value, partner.Value);
    }

    private async Task<(Context? Context, JointChatSession? Session, DomainError? Error)> ParticipantSessionAsync(
        Guid userId, Guid sessionId, CancellationToken cancellationToken, bool requireSafe = true)
    {
        var context = await ContextAsync(userId, cancellationToken);
        if (context is null)
        {
            return (null, null, DomainError.NotFound);
        }

        var session = await db.JointChatSessions.FirstOrDefaultAsync(
            s => s.Id == sessionId && s.WorkspaceId == context.WorkspaceId && s.Status != JointChatStatus.Ended, cancellationToken);
        if (session is null || !session.Involves(userId))
        {
            return (context, null, DomainError.NotFound);
        }

        if (requireSafe && await safety.UserBlocksJointFlowsAsync(userId, cancellationToken))
        {
            await EndQuietlyAsync(session, cancellationToken);
            return (context, null, DomainError.SafetyHold);
        }

        if (await RefreshAsync(session, context, cancellationToken))
        {
            await SaveAndNotifyAsync(session, cancellationToken);
            if (session.Status == JointChatStatus.Ended)
            {
                return (context, null, DomainError.Conflict);
            }
        }

        return (context, session, null);
    }

    /// <summary>
    /// Applies the time rules and ends a running chat if it may no longer continue: the pair has
    /// changed or either partner's safety check now keeps joint flows off. Returns true if the
    /// session changed.
    /// </summary>
    private async Task<bool> RefreshAsync(JointChatSession session, Context context, CancellationToken cancellationToken)
    {
        if (session.Status == JointChatStatus.Ended)
        {
            return false;
        }

        var now = clock.UtcNow;
        var changed = session.ApplyTimeRules(now);
        if (session.Status == JointChatStatus.Ended)
        {
            return changed;
        }

        var samePair = session.Involves(context.PartnerUserId);
        if (!samePair || (session.Status == JointChatStatus.Active && !await safety.WorkspaceAllowsJointFlowsAsync(session.WorkspaceId, cancellationToken)))
        {
            session.EndUnavailable(now);
            return true;
        }

        return changed;
    }

    /// <summary>Ends running chats the user is in when joint chat is no longer offered to them (but the pilot is on).</summary>
    private async Task EndOrphanedAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (!await flags.IsEnabledAsync(FeatureFlags.Phase2JointChat, cancellationToken))
        {
            return;
        }

        var open = await db.JointChatSessions
            .Where(s => s.Status != JointChatStatus.Ended && (s.InitiatorUserId == userId || s.InviteeUserId == userId))
            .ToListAsync(cancellationToken);
        foreach (var session in open)
        {
            await EndQuietlyAsync(session, cancellationToken);
        }
    }

    /// <summary>
    /// Ends a running chat without a reason. An unanswered invitation is left to lapse instead, so
    /// that it ends the same way whether or not a safety hold was the cause.
    /// </summary>
    private async Task EndQuietlyAsync(JointChatSession? session, CancellationToken cancellationToken)
    {
        if (session is not { Status: JointChatStatus.Active })
        {
            return;
        }

        session.EndUnavailable(clock.UtcNow);
        await SaveAndNotifyAsync(session, cancellationToken);
    }

    private Task<JointChatSession?> OpenSessionAsync(Guid workspaceId, CancellationToken cancellationToken) =>
        db.JointChatSessions.FirstOrDefaultAsync(s => s.WorkspaceId == workspaceId && s.Status != JointChatStatus.Ended, cancellationToken);

    private void RecordConsent(Guid userId, JointChatSession session) =>
        db.ConsentRecords.Add(ConsentRecord.Grant(
            userId, session.WorkspaceId, ConsentPurpose.JointChatSession, session.Id, ContentFingerprint.Of(session.PolicyVersion), clock.UtcNow));

    private async Task<Result<JointChatView>> SaveAndViewAsync(Guid userId, Context context, JointChatSession session, CancellationToken cancellationToken)
    {
        var saved = await SaveAsync(cancellationToken);
        if (!saved.Succeeded)
        {
            return saved.Error!.Value;
        }

        notifier.Notify(session.Id);
        return Result.Ok(await ViewAsync(userId, context, cancellationToken));
    }

    private async Task SaveAndNotifyAsync(JointChatSession session, CancellationToken cancellationToken)
    {
        if ((await SaveAsync(cancellationToken)).Succeeded)
        {
            notifier.Notify(session.Id);
        }
    }

    private async Task<JointChatView> ViewAsync(Guid userId, Context context, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var visibleSince = now - JointChatPolicy.TranscriptKeptAfterEnd;
        var session = await db.JointChatSessions.AsNoTracking()
            .Where(s => s.WorkspaceId == context.WorkspaceId
                && (s.InitiatorUserId == userId || s.InviteeUserId == userId)
                && (s.Status != JointChatStatus.Ended || s.EndedAt > visibleSince))
            .OrderBy(s => s.Status == JointChatStatus.Ended)
            .ThenByDescending(s => s.EndedAt)
            .ThenByDescending(s => s.InvitedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (session is null)
        {
            return JointChatView.Of(JointChatState.Ready);
        }

        var transcript = session.Status == JointChatStatus.Invited
            ? []
            : await db.JointChatMessages.AsNoTracking()
                .Where(m => m.SessionId == session.Id)
                .OrderBy(m => m.SentAt).ThenBy(m => m.Id)
                .Select(m => new JointChatLine(m.Id, m.AuthorUserId == userId, m.Body, m.SentAt))
                .ToListAsync(cancellationToken);

        var state = session.Status switch
        {
            JointChatStatus.Invited when session.InitiatorUserId == userId => JointChatState.WaitingForPartner,
            JointChatStatus.Invited => JointChatState.InvitedByPartner,
            JointChatStatus.Active => JointChatState.Active,
            _ => JointChatState.Ended,
        };

        JointChatEnding? ending = session.Status != JointChatStatus.Ended ? null : session.EndReason switch
        {
            JointChatEndReason.Left when session.EndedByUserId == userId => JointChatEnding.YouLeft,
            JointChatEndReason.Declined when session.EndedByUserId == userId => JointChatEnding.YouDeclined,
            JointChatEndReason.InvitationExpired when session.InitiatorUserId == userId => JointChatEnding.NotJoined,
            _ => JointChatEnding.Ended,
        };

        return new JointChatView(
            state,
            session.Id,
            MyTurn: session.Status == JointChatStatus.Active && session.CanWrite(userId, now),
            OpenTurn: session.Status == JointChatStatus.Active && session.TurnUserId is null,
            Paused: session.IsPaused(now),
            PausedByMe: session.IsPaused(now) && session.PausedByUserId == userId,
            PausedUntil: session.IsPaused(now) ? session.PausedUntil : null,
            InvitationExpiresAt: session.Status == JointChatStatus.Invited ? session.InvitedAt + JointChatPolicy.InvitationExpiry : null,
            EndsBy: session.StartedAt is { } started && session.Status == JointChatStatus.Active ? started + JointChatPolicy.MaxDuration : null,
            Ending: ending,
            TranscriptDeletedAt: session.EndedAt is { } ended ? ended + JointChatPolicy.TranscriptKeptAfterEnd : null,
            Transcript: transcript);
    }

    private async Task<Result> SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return Result.Ok();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Fail(DomainError.Conflict);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Result.Fail(DomainError.Conflict);
        }
    }
}
