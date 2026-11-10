using Couples.Domain.Common;
using Couples.Domain.Privacy;

namespace Couples.Domain.Rewards;

/// <summary>
/// A couple's shared credit balance, the area they want date rewards near, and how far weekly
/// scoring has got. One per workspace; both partners see the same balance. Holds no message text
/// and no per-person figures.
/// </summary>
[Privacy(DataClassification.Personal, "Kept while the workspace exists; deleted with the workspace. Reset when the pair of partners changes.")]
public sealed class CoupleRewardAccount
{
    public const int MaxAreaLength = 80;

    private CoupleRewardAccount()
    {
    }

    public Guid WorkspaceId { get; private set; }

    public int Balance { get; private set; }

    /// <summary>The first week not yet scored. Weeks before it have been turned into credits.</summary>
    public DateOnly NextWeekToScore { get; private set; }

    /// <summary>When the current pair of partners was formed (the later of their join times).</summary>
    public DateTimeOffset PairSince { get; private set; }

    /// <summary>Town or area for local rewards, as the couple typed it. Null shows rewards available anywhere.</summary>
    public string? Area { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>How many finished weeks a new account looks back over, so a couple starts with something.</summary>
    public const int WelcomeLookbackWeeks = 4;

    public static CoupleRewardAccount Open(Guid workspaceId, DateTimeOffset pairSince, DateTimeOffset now)
    {
        var account = new CoupleRewardAccount { WorkspaceId = workspaceId };
        account.StartPair(pairSince, now);
        return account;
    }

    /// <summary>A different pair of partners starts from zero; credits never pass to someone new.</summary>
    public void StartPair(DateTimeOffset pairSince, DateTimeOffset now)
    {
        var lookback = ConnectionScore.WeekStartOf(now).AddDays(-7 * WelcomeLookbackWeeks);
        var pairWeek = ConnectionScore.WeekStartOf(pairSince);
        PairSince = pairSince;
        Balance = 0;
        var start = pairWeek > lookback ? pairWeek : lookback;

        // Weeks the previous pair already scored are never scored again.
        NextWeekToScore = start > NextWeekToScore ? start : NextWeekToScore;
        UpdatedAt = now;
    }

    public void Earn(int credits, DateOnly scoredWeek, DateTimeOffset now)
    {
        Balance += Math.Max(0, credits);
        NextWeekToScore = scoredWeek.AddDays(7);
        UpdatedAt = now;
    }

    public Result Spend(int credits, DateTimeOffset now)
    {
        if (credits <= 0 || credits > Balance)
        {
            return Result.Fail(DomainError.Validation);
        }

        Balance -= credits;
        UpdatedAt = now;
        return Result.Ok();
    }

    /// <summary>Gives credits back when a provider couldn't issue the voucher they were reserved for.</summary>
    public void Refund(int credits, DateTimeOffset now)
    {
        Balance += Math.Max(0, credits);
        UpdatedAt = now;
    }

    public Result SetArea(string? area, DateTimeOffset now)
    {
        var trimmed = string.IsNullOrWhiteSpace(area) ? null : area.Trim();
        if (trimmed is { Length: > MaxAreaLength })
        {
            return Result.Fail(DomainError.Validation);
        }

        Area = trimmed;
        UpdatedAt = now;
        return Result.Ok();
    }
}
