using Couples.Domain.Common;
using Couples.Domain.Privacy;

namespace Couples.Domain.Auditing;

/// <summary>
/// Security-relevant action, recorded with ids only. Must never contain message text, reflection
/// text, names, emails or tokens.
/// </summary>
[Privacy(DataClassification.Internal, "Kept 400 days, then purged by the retention job. Ids of deleted users remain as unlinkable random values.")]
public sealed class AuditEvent : Entity
{
    private AuditEvent()
    {
    }

    public Guid ActorUserId { get; private set; }

    public Guid? WorkspaceId { get; private set; }

    public string Action { get; private set; } = string.Empty;

    public Guid? SubjectId { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public static AuditEvent Record(Guid actorUserId, string action, Guid? workspaceId, Guid? subjectId, DateTimeOffset now)
    {
        var audit = new AuditEvent
        {
            ActorUserId = actorUserId,
            Action = action,
            WorkspaceId = workspaceId,
            SubjectId = subjectId,
            OccurredAt = now,
        };
        audit.Touch(now);
        return audit;
    }
}

/// <summary>Audit action names. Stable strings: reports and retention rely on them.</summary>
public static class AuditActions
{
    public const string MessageShared = "message.shared";
    public const string MessageRevoked = "message.revoked";
    public const string DataExported = "data.exported";
    public const string DeletionRequested = "data.deletion_requested";
    public const string DeletionCompleted = "data.deletion_completed";
    public const string ExerciseReviewed = "exercise.reviewed";
    public const string PilotEnrolled = "pilot.enrolled";
    public const string PilotWithdrawn = "pilot.withdrawn";
    public const string JointChatEligibilityGranted = "jointchat.eligibility_granted";
    public const string JointChatEligibilityRevoked = "jointchat.eligibility_revoked";
    public const string JointChatInvited = "jointchat.invited";
    public const string JointChatJoined = "jointchat.joined";
    public const string JointChatEnded = "jointchat.ended";
    public const string RewardRedeemed = "reward.redeemed";
}
