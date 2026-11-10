using Couples.Domain.Common;
using Couples.Domain.Privacy;

namespace Couples.Domain.Rewards;

/// <summary>
/// One line in a couple's credit history: credits earned for a finished week (with that week's
/// shared score) or spent on a reward. Couple-level figures only.
/// </summary>
[Privacy(DataClassification.Personal, "Kept while the workspace exists; deleted with the workspace.")]
public sealed class CreditEntry : Entity
{
    private CreditEntry()
    {
    }

    public Guid WorkspaceId { get; private set; }

    public CreditEntryKind Kind { get; private set; }

    /// <summary>Positive when earned, negative when spent.</summary>
    public int Amount { get; private set; }

    public DateOnly? WeekStart { get; private set; }

    public int? WeekScore { get; private set; }

    public Guid? RedemptionId { get; private set; }

    public static CreditEntry Earned(Guid workspaceId, WeekScore week, int credits, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(week);
        var entry = new CreditEntry { WorkspaceId = workspaceId, Kind = CreditEntryKind.WeeklyEarned, Amount = credits, WeekStart = week.WeekStart, WeekScore = week.Total };
        entry.Touch(now);
        return entry;
    }

    public static CreditEntry Spent(Guid workspaceId, Guid redemptionId, int credits, DateTimeOffset now)
    {
        var entry = new CreditEntry { WorkspaceId = workspaceId, Kind = CreditEntryKind.Redeemed, Amount = -credits, RedemptionId = redemptionId };
        entry.Touch(now);
        return entry;
    }
}

public enum CreditEntryKind
{
    WeeklyEarned = 1,
    Redeemed = 2,
}
