using Couples.Domain.Common;

namespace Couples.Domain.Privacy;

/// <summary>
/// A user's request to erase their data. Processed by the deletion workflow (web request first,
/// worker as a fallback). The row itself holds only ids and timestamps.
/// </summary>
[Privacy(DataClassification.Internal, "Kept 400 days after completion as evidence the request was honoured, then purged.")]
public sealed class DataDeletionRequest : Entity
{
    private DataDeletionRequest()
    {
    }

    public Guid UserId { get; private set; }

    public DateTimeOffset RequestedAt { get; private set; }

    public DeletionStatus Status { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public int Attempts { get; private set; }

    public static DataDeletionRequest Create(Guid userId, DateTimeOffset now)
    {
        var request = new DataDeletionRequest { UserId = userId, RequestedAt = now, Status = DeletionStatus.Pending };
        request.Touch(now);
        return request;
    }

    public void RecordAttempt(DateTimeOffset now)
    {
        Attempts++;
        Touch(now);
    }

    public void Complete(DateTimeOffset now)
    {
        Status = DeletionStatus.Completed;
        CompletedAt = now;
        Touch(now);
    }
}

public enum DeletionStatus
{
    Pending = 1,
    Completed = 2,
}
