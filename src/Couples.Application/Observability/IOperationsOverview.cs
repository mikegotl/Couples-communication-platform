namespace Couples.Application.Observability;

/// <summary>
/// Aggregate counts for operators. Any count below <see cref="SuppressBelow"/> is reported as
/// null ("fewer than 5") so small numbers can't single anyone out.
/// </summary>
public sealed record OperationsSnapshot(
    long? Accounts,
    long? PilotParticipants,
    long? PilotWithdrawn,
    long? ActiveWorkspaces,
    long? MessagesSharedLast7Days,
    long? FeedbackLast30Days,
    long? PendingDeletions)
{
    public const int SuppressBelow = 5;
}

public interface IOperationsOverview
{
    Task<OperationsSnapshot> GetAsync(CancellationToken cancellationToken = default);
}
