using Couples.Domain.Common;
using Couples.Domain.Privacy;

namespace Couples.Domain.Notifications;

/// <summary>
/// An in-app notice that something arrived. Metadata only: the kind and the id of the message,
/// never its text or anything derived from it. Shown from <see cref="VisibleAt"/>, which respects
/// the recipient's quiet hours. Removed if the message is withdrawn.
/// </summary>
[Privacy(DataClassification.Personal, "Purged 90 days after creation; removed when its message is withdrawn or deleted; deleted with the account.")]
public sealed class Notification : Entity
{
    private Notification()
    {
    }

    public Guid RecipientUserId { get; private set; }

    public NotificationKind Kind { get; private set; }

    public Guid SubjectId { get; private set; }

    public DateTimeOffset VisibleAt { get; private set; }

    public DateTimeOffset? ReadAt { get; private set; }

    public static Notification Create(Guid recipientUserId, NotificationKind kind, Guid subjectId, DateTimeOffset visibleAt, DateTimeOffset now)
    {
        var notification = new Notification { RecipientUserId = recipientUserId, Kind = kind, SubjectId = subjectId, VisibleAt = visibleAt };
        notification.Touch(now);
        return notification;
    }

    public void MarkRead(DateTimeOffset now)
    {
        ReadAt ??= now;
        Touch(now);
    }
}

public enum NotificationKind
{
    NewMessage = 1,
    Reply = 2,
}
