using Couples.Application;
using Couples.Application.Abstractions;
using Couples.Application.Rewards;
using Couples.Application.Safety;
using Couples.Domain.Auditing;
using Couples.Domain.Common;
using Couples.Domain.Rewards;
using Couples.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Couples.Infrastructure.Rewards;

/// <summary>
/// Scores the couple's shared messages week by week and turns finished weeks into shared credits
/// (ADR-0017). Message text is read in memory to spot warm wording and is never stored, logged or
/// returned; only weekly couple-level figures are kept. Logs nothing, so a safety hold leaves no trace.
/// </summary>
internal sealed class ConnectionProgressService(
    CouplesDbContext db,
    IClock clock,
    IFeatureFlags flags,
    ISafetyGate safety,
    IRewardCatalogProvider provider) : IConnectionProgressService
{
    private const int RecentWeekCount = 8;
    private const int MaxCatchUpWeeks = 26;

    public async Task<ConnectionProgress?> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var pair = await PairAsync(userId, cancellationToken);
        if (pair is null)
        {
            return null;
        }

        var now = clock.UtcNow;
        var thisWeek = ConnectionScore.WeekStartOf(now);
        if (await safety.UserBlocksJointFlowsAsync(userId, cancellationToken))
        {
            // Someone whose own check-in or safety mode holds joint features sees no score and no
            // nudges to talk more. Only they see this view, so it reveals nothing to the partner.
            return new ConnectionProgress(0, null, ConnectionScore.Score(thisWeek, []), [], 0, ProgressTip.KeepItUp, ShowProgress: false, Rewards: null);
        }

        var rewardsOn = await flags.IsEnabledAsync(FeatureFlags.DateRewards, cancellationToken)
            && await safety.WorkspaceAllowsJointFlowsAsync(pair.WorkspaceId, cancellationToken);

        CoupleRewardAccount? account = null;
        var from = now.AddDays(-7 * (RecentWeekCount + 1));
        if (rewardsOn)
        {
            account = await OpenAccountAsync(pair, now, cancellationToken);
            from = Min(from, ConnectionScore.StartOf(account.NextWeekToScore).AddDays(-7));
        }

        var signals = await SignalsAsync(pair, from, cancellationToken);

        if (account is not null)
        {
            account = await AwardFinishedWeeksAsync(pair, account, signals, thisWeek, now, cancellationToken);
        }

        var firstWeek = ConnectionScore.WeekStartOf(pair.Since);
        var recent = Enumerable.Range(1, RecentWeekCount)
            .Select(i => thisWeek.AddDays(-7 * i))
            .Where(w => w >= firstWeek)
            .Select(w => ConnectionScore.Score(w, signals))
            .OrderBy(w => w.WeekStart)
            .ToList();
        var soFar = ConnectionScore.Score(thisWeek, signals);
        var latest = recent.TakeLast(4).ToList();
        var earlier = recent.SkipLast(4).TakeLast(4).ToList();
        var score = latest.Count == 0 ? soFar.Total : (int)Math.Round(latest.Average(w => w.Total));
        int? previous = earlier.Count == 0 ? null : (int)Math.Round(earlier.Average(w => w.Total));
        var quiet = ConnectionScore.LongestQuietStretch(signals, Max(pair.Since, now.AddDays(-28)), now);
        var tip = TipFor(recent.LastOrDefault() ?? soFar);

        var rewards = account is null ? null : await RewardsAsync(userId, account, cancellationToken);
        return new ConnectionProgress(score, previous, soFar, recent, quiet, tip, ShowProgress: true, rewards);
    }

    public async Task<Result<RedemptionView>> RedeemAsync(Guid userId, Guid offerId, CancellationToken cancellationToken = default)
    {
        db.ChangeTracker.Clear();
        var pair = await PairAsync(userId, cancellationToken);
        if (pair is null)
        {
            return DomainError.NoPartner;
        }

        if (!await flags.IsEnabledAsync(FeatureFlags.DateRewards, cancellationToken)
            || !await safety.WorkspaceAllowsJointFlowsAsync(pair.WorkspaceId, cancellationToken))
        {
            return DomainError.NotFound;
        }

        var account = await db.RewardAccounts.FirstOrDefaultAsync(a => a.WorkspaceId == pair.WorkspaceId, cancellationToken);
        var offer = await provider.FindOfferAsync(offerId, cancellationToken);
        if (account is null || offer is null || !offer.IsActive || !offer.AvailableIn(account.Area))
        {
            return DomainError.NotFound;
        }

        var now = clock.UtcNow;
        if (!account.Spend(offer.CostCredits, now).Succeeded)
        {
            return DomainError.Validation;
        }

        // Reserve first: the balance is committed (or the race lost) before any provider is asked.
        var redemption = RewardRedemption.Reserve(pair.WorkspaceId, offer, userId, provider.Name, now);
        var spent = CreditEntry.Spent(pair.WorkspaceId, redemption.Id, offer.CostCredits, now);
        db.RewardRedemptions.Add(redemption);
        db.CreditEntries.Add(spent);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return DomainError.Conflict;
        }

        var issued = await provider.IssueAsync(offer, redemption.Id, cancellationToken);
        if (!issued.Succeeded)
        {
            account.Refund(offer.CostCredits, clock.UtcNow);
            db.CreditEntries.Remove(spent);
            db.RewardRedemptions.Remove(redemption);
            await db.SaveChangesAsync(CancellationToken.None);
            return issued.Error!.Value;
        }

        redemption.Issue(issued.Value.Code, issued.Value.Instructions, issued.Value.ExpiresAt, clock.UtcNow);
        db.AuditEvents.Add(AuditEvent.Record(userId, AuditActions.RewardRedeemed, pair.WorkspaceId, redemption.Id, clock.UtcNow));
        await db.SaveChangesAsync(CancellationToken.None);
        return Result.Ok(ToView(redemption, userId));
    }

    public async Task<Result> SetAreaAsync(Guid userId, string? area, CancellationToken cancellationToken = default)
    {
        db.ChangeTracker.Clear();
        var pair = await PairAsync(userId, cancellationToken);
        if (pair is null)
        {
            return Result.Fail(DomainError.NoPartner);
        }

        if (!await flags.IsEnabledAsync(FeatureFlags.DateRewards, cancellationToken)
            || !await safety.WorkspaceAllowsJointFlowsAsync(pair.WorkspaceId, cancellationToken))
        {
            return Result.Fail(DomainError.NotFound);
        }

        var account = await OpenAccountAsync(pair, clock.UtcNow, cancellationToken);
        var set = account.SetArea(area, clock.UtcNow);
        if (!set.Succeeded)
        {
            return set;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return Result.Ok();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Fail(DomainError.Conflict);
        }
    }

    private static ProgressTip TipFor(WeekScore week)
    {
        if (week.Total >= 80)
        {
            return ProgressTip.KeepItUp;
        }

        // The part with the most room to grow, as a share of its maximum.
        var gaps = new (ProgressTip Tip, double Room)[]
        {
            (ProgressTip.KindWords, 1 - (week.KindWords / (double)ConnectionScore.KindWordsMax)),
            (ProgressTip.BothVoices, 1 - (week.BothVoices / (double)ConnectionScore.BothVoicesMax)),
            (ProgressTip.StayInTouch, 1 - (week.StayingInTouch / (double)ConnectionScore.StayingInTouchMax)),
        };
        return gaps.OrderByDescending(g => g.Room).First().Tip;
    }

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;

    private static RedemptionView ToView(RewardRedemption r, Guid userId) =>
        new(r.Id, r.OfferTitle, r.PartnerName, r.CostCredits, r.VoucherCode, r.Instructions, r.IsSample, r.RedeemedByUserId == userId, r.CreatedAt, r.ExpiresAt);

    private static RewardOfferView ToView(RewardOffer o) =>
        new(o.Id, o.Title, o.PartnerName, o.Description, o.Area, o.Kind, o.CostCredits, o.IsSample);

    /// <summary>The user's active workspace and partner, or null if either is missing.</summary>
    private async Task<Pair?> PairAsync(Guid userId, CancellationToken cancellationToken)
    {
        var mine = await db.WorkspaceMemberships.AsNoTracking()
            .Where(m => m.UserId == userId && m.LeftAt == null)
            .Select(m => new { m.WorkspaceId, m.JoinedAt })
            .FirstOrDefaultAsync(cancellationToken);
        if (mine is null)
        {
            return null;
        }

        var partner = await db.WorkspaceMemberships.AsNoTracking()
            .Where(m => m.WorkspaceId == mine.WorkspaceId && m.UserId != userId && m.LeftAt == null)
            .Select(m => new { m.UserId, m.JoinedAt })
            .FirstOrDefaultAsync(cancellationToken);
        return partner is null
            ? null
            : new Pair(mine.WorkspaceId, userId, partner.UserId, mine.JoinedAt > partner.JoinedAt ? mine.JoinedAt : partner.JoinedAt);
    }

    /// <summary>Messages between the two current partners. Revoked messages don't count.</summary>
    private async Task<List<MessageSignal>> SignalsAsync(Pair pair, DateTimeOffset from, CancellationToken cancellationToken)
    {
        var rows = await db.SharedMessages.AsNoTracking()
            .Where(m => m.WorkspaceId == pair.WorkspaceId && m.RevokedAt == null && m.SharedAt >= from
                && ((m.SenderUserId == pair.Me && m.RecipientUserId == pair.Partner) || (m.SenderUserId == pair.Partner && m.RecipientUserId == pair.Me)))
            .Select(m => new { m.SharedAt, m.SenderUserId, IsReply = m.InReplyToMessageId != null, m.Body })
            .ToListAsync(cancellationToken);
        return [.. rows.Select(r => new MessageSignal(r.SharedAt, r.SenderUserId, r.IsReply, WarmLanguage.IsWarm(r.Body)))];
    }

    private async Task<CoupleRewardAccount> OpenAccountAsync(Pair pair, DateTimeOffset now, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            var account = await db.RewardAccounts.FirstOrDefaultAsync(a => a.WorkspaceId == pair.WorkspaceId, cancellationToken);
            if (account is not null && account.PairSince == pair.Since)
            {
                return account;
            }

            if (account is null)
            {
                db.RewardAccounts.Add(CoupleRewardAccount.Open(pair.WorkspaceId, pair.Since, now));
            }
            else
            {
                account.StartPair(pair.Since, now);
            }

            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException) when (attempt < 2)
            {
                // The partner opened or reset it at the same moment; use theirs.
                db.ChangeTracker.Clear();
            }
        }
    }

    private async Task<CoupleRewardAccount> AwardFinishedWeeksAsync(Pair pair, CoupleRewardAccount account, List<MessageSignal> signals, DateOnly thisWeek, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (account.NextWeekToScore >= thisWeek)
        {
            return account;
        }

        var firstWeek = ConnectionScore.WeekStartOf(pair.Since);
        var start = account.NextWeekToScore < thisWeek.AddDays(-7 * MaxCatchUpWeeks) ? thisWeek.AddDays(-7 * MaxCatchUpWeeks) : account.NextWeekToScore;
        for (var week = start; week < thisWeek; week = week.AddDays(7))
        {
            var score = ConnectionScore.Score(week, signals);
            var previous = week.AddDays(-7) >= firstWeek ? ConnectionScore.Score(week.AddDays(-7), signals) : null;
            var credits = ConnectionScore.CreditsFor(score, previous);
            db.CreditEntries.Add(CreditEntry.Earned(pair.WorkspaceId, score, credits, now));
            account.Earn(credits, week, now);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return account;
        }
        catch (DbUpdateException)
        {
            // The partner scored the same weeks at the same moment; their result stands.
            db.ChangeTracker.Clear();
            return await db.RewardAccounts.FirstAsync(a => a.WorkspaceId == pair.WorkspaceId, cancellationToken);
        }
    }

    private async Task<RewardsView> RewardsAsync(Guid userId, CoupleRewardAccount account, CancellationToken cancellationToken)
    {
        var offers = (await provider.ListOffersAsync(account.Area, cancellationToken)).Select(ToView).ToList();
        var suggestion = offers.Where(o => o.CostCredits <= account.Balance).MaxBy(o => o.CostCredits);
        var nextGoal = offers.Where(o => o.CostCredits > account.Balance).MinBy(o => o.CostCredits);

        var redemptions = await db.RewardRedemptions.AsNoTracking()
            .Where(r => r.WorkspaceId == account.WorkspaceId && r.Status == RedemptionStatus.Issued && r.CreatedAt >= account.PairSince)
            .OrderByDescending(r => r.CreatedAt)
            .Take(20)
            .ToListAsync(cancellationToken);
        var titles = redemptions.ToDictionary(r => r.Id, r => r.OfferTitle);
        var entries = await db.CreditEntries.AsNoTracking()
            .Where(e => e.WorkspaceId == account.WorkspaceId && e.CreatedAt >= account.PairSince)
            .OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.WeekStart)
            .Take(12)
            .ToListAsync(cancellationToken);
        var history = entries
            .Select(e => new CreditHistoryLine(e.CreatedAt, e.Amount, e.WeekStart, e.WeekScore, e.RedemptionId is { } id && titles.TryGetValue(id, out var t) ? t : null))
            .ToList();

        return new RewardsView(account.Balance, account.Area, offers, suggestion, nextGoal, history, [.. redemptions.Select(r => ToView(r, userId))]);
    }

    private sealed record Pair(Guid WorkspaceId, Guid Me, Guid Partner, DateTimeOffset Since);
}
