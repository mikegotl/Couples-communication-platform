using Couples.Domain.Common;
using Couples.Domain.Rewards;

namespace Couples.Application.Rewards;

/// <summary>
/// The couple's shared connection score, their credits and date rewards (ADR-0017). Scores use
/// shared messages only (never drafts, reflections, intake, exercises or safety answers) and are
/// the same for both partners. Credits are earned only while the DateRewards flag is on and both
/// partners' safety settings allow joint features; nothing here says why rewards are unavailable.
/// </summary>
public interface IConnectionProgressService
{
    /// <summary>Scores any finished weeks not yet turned into credits, then returns the dashboard. Null without a partner.</summary>
    Task<ConnectionProgress?> GetAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Spends shared credits on an offer and returns the voucher. Fails with Conflict if the balance changed meanwhile.</summary>
    Task<Result<RedemptionView>> RedeemAsync(Guid userId, Guid offerId, CancellationToken cancellationToken = default);

    Task<Result> SetAreaAsync(Guid userId, string? area, CancellationToken cancellationToken = default);
}

/// <summary>What either partner sees. Nothing in it is per person.</summary>
public sealed record ConnectionProgress(
    int Score,
    int? PreviousScore,
    WeekScore ThisWeekSoFar,
    IReadOnlyList<WeekScore> RecentWeeks,
    int LongestQuietStretchDays,
    ProgressTip Tip,
    bool ShowProgress,
    RewardsView? Rewards);

/// <summary>Null on <see cref="ConnectionProgress"/> when rewards aren't available to this couple right now.</summary>
public sealed record RewardsView(
    int Balance,
    string? Area,
    IReadOnlyList<RewardOfferView> Offers,
    RewardOfferView? Suggestion,
    RewardOfferView? NextGoal,
    IReadOnlyList<CreditHistoryLine> History,
    IReadOnlyList<RedemptionView> Redemptions);

public sealed record RewardOfferView(Guid Id, string Title, string PartnerName, string Description, string? Area, RewardKind Kind, int CostCredits, bool IsSample);

public sealed record CreditHistoryLine(DateTimeOffset At, int Amount, DateOnly? WeekStart, int? WeekScore, string? RewardTitle);

public sealed record RedemptionView(Guid Id, string OfferTitle, string PartnerName, int CostCredits, string VoucherCode, string Instructions, bool IsSample, bool RedeemedByMe, DateTimeOffset RedeemedAt, DateTimeOffset? ExpiresAt);

public enum ProgressTip
{
    StayInTouch,
    BothVoices,
    KindWords,
    KeepItUp,
}
