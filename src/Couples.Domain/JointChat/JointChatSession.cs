using Couples.Domain.Common;
using Couples.Domain.Privacy;

namespace Couples.Domain.JointChat;

public enum JointChatStatus
{
    Invited = 1,
    Active = 2,
    Ended = 3,
}

/// <summary>
/// Why a chat ended. Only <see cref="Left"/> and <see cref="Declined"/> are shown to the person who
/// did it; the other partner always sees a neutral "This chat has ended", so an ending caused by a
/// safety hold or moderation can't be told apart from any other.
/// </summary>
public enum JointChatEndReason
{
    Left = 1,
    Declined = 2,
    InvitationExpired = 3,
    Idle = 4,
    TimeLimit = 5,

    /// <summary>Safety hold, moderation, eligibility revoked, membership ended or the pilot switched off.</summary>
    Unavailable = 6,
}

/// <summary>
/// One guided joint chat between the two partners of a workspace. Both must agree before it
/// starts (fresh consent each time, recorded in the consent ledger); either can leave at any
/// moment, which ends it for both. Partners take turns; either can pause for a cooling-off break.
/// </summary>
[Privacy(DataClassification.Personal, "Ids, times and state only. Deleted with its transcript 7 days after it ends, or when either partner deletes their account.")]
public sealed class JointChatSession : Entity
{
    private JointChatSession()
    {
    }

    public Guid WorkspaceId { get; private set; }

    public Guid InitiatorUserId { get; private set; }

    public Guid InviteeUserId { get; private set; }

    public string PolicyVersion { get; private set; } = string.Empty;

    public JointChatStatus Status { get; private set; }

    public DateTimeOffset InvitedAt { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? EndedAt { get; private set; }

    public JointChatEndReason? EndReason { get; private set; }

    /// <summary>Who ended it by leaving or declining, if anyone did.</summary>
    public Guid? EndedByUserId { get; private set; }

    /// <summary>Whose turn it is; null when either may write (after a turn timeout).</summary>
    public Guid? TurnUserId { get; private set; }

    public DateTimeOffset? TurnStartedAt { get; private set; }

    public Guid? PausedByUserId { get; private set; }

    public DateTimeOffset? PausedUntil { get; private set; }

    public DateTimeOffset LastActivityAt { get; private set; }

    public int MessageCount { get; private set; }

    public bool Involves(Guid userId) => userId == InitiatorUserId || userId == InviteeUserId;

    public Guid PartnerOf(Guid userId) => userId == InitiatorUserId ? InviteeUserId : InitiatorUserId;

    public bool IsPaused(DateTimeOffset now) => Status == JointChatStatus.Active && PausedUntil > now;

    public static Result<JointChatSession> Invite(Guid workspaceId, Guid initiatorUserId, Guid inviteeUserId, string policyVersion, DateTimeOffset now)
    {
        if (initiatorUserId == inviteeUserId)
        {
            return DomainError.Validation;
        }

        if (policyVersion != JointChatPolicy.Version)
        {
            return DomainError.ConsentMismatch;
        }

        var session = new JointChatSession
        {
            WorkspaceId = workspaceId,
            InitiatorUserId = initiatorUserId,
            InviteeUserId = inviteeUserId,
            PolicyVersion = policyVersion,
            Status = JointChatStatus.Invited,
            InvitedAt = now,
            LastActivityAt = now,
        };
        session.Touch(now);
        return Result.Ok(session);
    }

    /// <summary>The invitee agrees to this chat. The person who started it writes first.</summary>
    public Result Accept(Guid userId, string policyVersion, DateTimeOffset now)
    {
        if (Status != JointChatStatus.Invited || userId != InviteeUserId)
        {
            return Result.Fail(DomainError.Conflict);
        }

        if (policyVersion != PolicyVersion)
        {
            return Result.Fail(DomainError.ConsentMismatch);
        }

        Status = JointChatStatus.Active;
        StartedAt = now;
        TurnUserId = InitiatorUserId;
        TurnStartedAt = now;
        LastActivityAt = now;
        Touch(now);
        return Result.Ok();
    }

    public Result Decline(Guid userId, DateTimeOffset now) =>
        Status == JointChatStatus.Invited && userId == InviteeUserId
            ? EndInternal(JointChatEndReason.Declined, userId, now)
            : Result.Fail(DomainError.Conflict);

    /// <summary>Either partner, at any time, without the other's agreement. Ends the chat for both.</summary>
    public Result Leave(Guid userId, DateTimeOffset now) =>
        Status != JointChatStatus.Ended && Involves(userId)
            ? EndInternal(JointChatEndReason.Left, userId, now)
            : Result.Fail(DomainError.Conflict);

    /// <summary>Ends the chat without saying why (see <see cref="JointChatEndReason.Unavailable"/>).</summary>
    public void EndUnavailable(DateTimeOffset now)
    {
        if (Status != JointChatStatus.Ended)
        {
            EndInternal(JointChatEndReason.Unavailable, null, now);
        }
    }

    public Result Pause(Guid userId, int minutes, DateTimeOffset now)
    {
        if (Status != JointChatStatus.Active || !Involves(userId))
        {
            return Result.Fail(DomainError.Conflict);
        }

        if (!JointChatPolicy.PauseMinutes.Contains(minutes))
        {
            return Result.Fail(DomainError.Validation);
        }

        PausedByUserId = userId;
        PausedUntil = now.AddMinutes(minutes);
        LastActivityAt = now;
        Touch(now);
        return Result.Ok();
    }

    /// <summary>Only the person who paused can end the break early.</summary>
    public Result Resume(Guid userId, DateTimeOffset now)
    {
        if (!IsPaused(now) || PausedByUserId != userId)
        {
            return Result.Fail(DomainError.Conflict);
        }

        EndPause(now);
        return Result.Ok();
    }

    public bool CanWrite(Guid userId, DateTimeOffset now) =>
        Status == JointChatStatus.Active && Involves(userId) && !IsPaused(now) && (TurnUserId is null || TurnUserId == userId);

    /// <summary>Records that <paramref name="userId"/> sent a message; the turn passes to their partner.</summary>
    public Result RecordMessage(Guid userId, DateTimeOffset now)
    {
        if (!CanWrite(userId, now))
        {
            return Result.Fail(DomainError.Conflict);
        }

        MessageCount++;
        TurnUserId = PartnerOf(userId);
        TurnStartedAt = now;
        LastActivityAt = now;
        Touch(now);
        return Result.Ok();
    }

    /// <summary>
    /// Applies the time rules: lapsed invitations, finished pauses, turn timeouts, idle chats and
    /// the overall time limit. Returns true if anything changed.
    /// </summary>
    public bool ApplyTimeRules(DateTimeOffset now)
    {
        switch (Status)
        {
            case JointChatStatus.Invited when now - InvitedAt >= JointChatPolicy.InvitationExpiry:
                EndInternal(JointChatEndReason.InvitationExpired, null, now);
                return true;
            case JointChatStatus.Active:
                var changed = false;
                if (PausedUntil is { } until && until <= now)
                {
                    // The break counts as activity: the idle clock restarts when it ends.
                    LastActivityAt = Max(LastActivityAt, until);
                    TurnStartedAt = TurnStartedAt is { } started ? Max(started, until) : null;
                    PausedByUserId = null;
                    PausedUntil = null;
                    changed = true;
                }

                if (now - StartedAt!.Value >= JointChatPolicy.MaxDuration)
                {
                    EndInternal(JointChatEndReason.TimeLimit, null, now);
                    return true;
                }

                if (!IsPaused(now) && now - LastActivityAt >= JointChatPolicy.IdleTimeout)
                {
                    EndInternal(JointChatEndReason.Idle, null, now);
                    return true;
                }

                if (!IsPaused(now) && TurnUserId is not null && TurnStartedAt is { } turnStart && now - turnStart >= JointChatPolicy.TurnTimeout)
                {
                    TurnUserId = null;
                    changed = true;
                }

                if (changed)
                {
                    Touch(now);
                }

                return changed;
            default:
                return false;
        }
    }

    private void EndPause(DateTimeOffset now)
    {
        PausedByUserId = null;
        PausedUntil = null;
        LastActivityAt = now;
        TurnStartedAt = now;
        Touch(now);
    }

    private Result EndInternal(JointChatEndReason reason, Guid? byUserId, DateTimeOffset now)
    {
        Status = JointChatStatus.Ended;
        EndReason = reason;
        EndedByUserId = byUserId;
        EndedAt = now;
        TurnUserId = null;
        PausedByUserId = null;
        PausedUntil = null;
        Touch(now);
        return Result.Ok();
    }

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;
}
